---
name: udonsharp-editor-automation
description: UdonSharp editor-script automation and event/testing pitfalls for VRChat worlds. Use when programmatically creating/wiring UdonSharpBehaviour components from editor scripts (scene builders, prefab generation), binding uGUI Button events to Udon, firing Udon events via SendCustomEvent in automated tests, debugging "fields are null at runtime" after editor-script setup, fixing AddUdonSharpComponent NullReferenceException from stale UdonSharp program-asset caches, recompiling Udon programs from CLI, or attaching/detaching physics parts to a shared Rigidbody (compound collider patterns with VRCPickup). Complements uloop-udonsharp (which covers uloop CLI operations) with hard-won behavioral facts about UdonSharp internals.
---

# UdonSharp Editor Automation & Event Pitfalls

Hard-won facts about UdonSharp internals when automating scene setup or testing. Assumes SDK 3.x integrated UdonSharp (`Packages/com.vrchat.worlds/Integrations/UdonSharp`).

## 1. Editor scripts must sync proxy -> backing, or every field is null at runtime

Setting fields on a `UdonSharpBehaviour` proxy from an editor script does **not** propagate them to the backing `UdonBehaviour`. At runtime the Udon heap then holds an array full of nulls / zero-length arrays, and the first field read on another behaviour dies with:

```
Udon runtime exception detected!
An exception occurred during EXTERN to 'VRCUdonCommonInterfacesIUdonEventReceiver.__GetProgramVariable__SystemString__SystemObject'.
System.NullReferenceException
```

Correct procedure per component:

```csharp
// 1. Add with AddUdonSharpComponent<T>() - creates the backing UdonBehaviour.
//    Plain AddComponent<T>() leaves the proxy without backing until play mode.
var pb = go.AddUdonSharpComponent<PartBehaviour>();
// 2. Set fields on the proxy
pb.pool = pool;
// 3. Copy proxy -> backing
UdonSharpEditorUtility.CopyProxyToUdon(pb);
```

Rules:

- `AddUdonSharpComponent<T>()` (from `UdonSharpEditor`) wraps `AddComponent` + `RunBehaviourSetup`. `UdonSharpEditorUtility.CreateBehaviourForProxy` does NOT create the backing by itself despite the name.
- `CopyProxyToUdon` requires referenced proxies to already have backing behaviours - the formatter resolves cross-behaviour references through them (`Dictionary key null` NRE otherwise). Create all proxies first, then sync.
- Sync prefab contents BEFORE `PrefabUtility.SaveAsPrefabAsset`, and sync scene instances after per-instance field overrides.
- `CopyProxyToUdon` throws "outdated behaviour version" if program assets are stale - compile first.
- Also see: `.agents/skills/uloop-udonsharp/references/udonsharp-operations.md` sections 1/3/6/14.

## 2. uGUI Button.onClick cannot call UdonSharpBehaviour methods directly

Persistent listeners bound to the managed method (`UnityEventTools.AddVoidPersistentListener(btn.onClick, script.OnClick)`) do not fire into Udon. Bind `UdonBehaviour.SendCustomEvent` with the method name instead:

```csharp
UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(spawnBtn);
UnityEventTools.AddStringPersistentListener(btn.onClick, udon.SendCustomEvent, "OnClick");
```

`OnClick` here is a custom public method - custom methods keep their exact name as entry points.

## 3. Built-in event entry points are renamed: `OnPickup` -> `_onPickup`

UdonSharp compiles built-in Udon event overrides to entry points named `"_" + camelCase`:

| C# override | Entry point for SendCustomEvent |
|---|---|
| `OnPickup()` | `_onPickup` |
| `OnDrop()` | `_onDrop` |
| `OnDeserialization()` | `_onDeserialization` |
| `Update()` | `_update` |
| `Start()` | `_start` |

(Implementation: `CompilerUdonInterface` `_builtinEventLookup`, built from `Event_` node definitions.)

Consequences:

- Real gameplay is unaffected - the VRC SDK fires the renamed events correctly.
- Automated tests (uloop execute-dynamic-code) MUST use the underscore names: `udon.SendCustomEvent("_onPickup")`.
- Custom public methods are NOT renamed: `SendCustomEvent("OnClick")` works as-is.
- `SendCustomEvent` with a nonexistent name fails silently - no error, no log. If an event "does nothing", suspect the name first.

## 4. `uloop compile` does not compile Udon programs

C# compile and UdonSharp->Udon bytecode compile are separate. After editing UdonSharp scripts:

```csharp
UdonSharpCompilerV1.CompileSync();
return UdonSharpProgramAsset.AnyUdonSharpScriptHasError().ToString();
```

A stale program runs the OLD bytecode with no warning - new `Debug.Log` lines not appearing in console is the tell. (In this project `Assets/Editor/UdonSharpProgramAssetAutoGenerator.cs` creates program assets for NEW scripts and compiles only then; modified scripts need the explicit `CompileSync()`.)

## 5. VRCPickup silently requires a Rigidbody - plan physics around it

`AddComponent<VRCPickup>()` auto-adds a non-kinematic Rigidbody (gravity on). For parts that attach to a machine with its own Rigidbody:

- A non-kinematic child Rigidbody does NOT join the parent's compound collider and fights the parent's transform -> jitter.
- Attach: `partRigidbody.isKinematic = true` -> kinematic child colliders DO join the parent compound collider (one physics body per machine).
- Detach: restore `isKinematic = false`.
- Free/pooled parts will physically fall when spawned mid-air; spawn low or accept the drop.
- Use VRChat's built-in **Pickup layer (13)** for pickup parts, permanently - do not invent a custom "held" layer or switch layers on pickup/drop. The SDK's collision matrix already treats layer 13 correctly, and custom layers + `DynamicsManager.asset` bit edits are over-engineering that must be reverted later.
- VRCPickup objects are **moved to layer 13 automatically at runtime** (ClientSim included), even if the editor scene has them on Default. When debugging "the pickup fell through the floor / never collides", check the live layer first, and remember collision callbacks (OnCollisionStay harvesting etc.) run under the Pickup layer's matrix row, not Default's.
- Ground/terrain pieces must have colliders wherever pooled physics objects (pickups, tools) can land - a visual-only ground cube lets them fall through silently.

## 6. WheelCollider works in Udon (verified SDK 3.10.5)

- `UnityEngine.WheelCollider` is in the world allowed-components list (`WorldValidation.cs`).
- The SDK ships a UdonSharp regression test driving `motorTorque` / `steerAngle` / `brakeTorque` (`Integrations/UdonSharp/Tests~/TestScripts/RegressionTests/DebugCarSystemWorking.cs`).
- Joints (Hinge/Spring/Fixed/Character/Configurable) are also allowed components, but `AddComponent` at runtime is impossible in Udon - everything must pre-exist on prefabs.

## 7. Play mode state: the Udon heap is the truth, proxies go stale

- Reading public fields on the proxy in play mode returns edit-time values, not runtime values. Use `udon.GetProgramVariable("fieldName")`, or `CopyUdonToProxy(proxy, ProxySerializationPolicy.All)` before reading.
- Calling proxy methods in play mode runs C# against stale fields (private fields are defaults) - fine for structural verification, never for logic verification. Drive logic via `SendCustomEvent` instead.
- Unity-serialized array fields default to **length 0, not null** - null-checks are not enough; check `Length`.

## 8. Pooling is mandatory

`Object.Instantiate` does not exist in Udon. Pre-place all parts in the scene (inactive), hand out from a pool, and treat the pool index as the network-stable identity of each part for `[UdonSynced]` build data.

**Start() timing trap for pooled objects**: on a GameObject that was inactive at scene load, `Start` does not run synchronously on `SetActive(true)` - it fires next frame. Event handlers that run before it (e.g. `_onPickup` immediately after spawning) see null component references. Lazy-init in every event handler instead of trusting `Start`:

```csharp
private Rigidbody _rb;
private void EnsureInit()
{
    if (_rb == null) _rb = GetComponent<Rigidbody>();
}

public override void OnPickup()
{
    EnsureInit();
    // safe to use _rb here
}
```

## 9. `AddUdonSharpComponent` NREs when the program-asset cache is stale

Symptom: `AddUdonSharpComponent<T>()` throws `NullReferenceException` inside `UdonSharpEditorUtility.RunBehaviourSetup` (around `UdonSharpEditorUtility.cs:904`, `serializedProgramAssetProperty.objectReferenceValue = programAsset.SerializedProgramAsset`) even though the `.asset` program asset exists next to the `.cs` and `CompileSync()` reports no errors.

Cause: UdonSharp resolves the program asset for a proxy through two static caches - `UdonSharpProgramAsset._programAssetCache` (all program assets) and `UdonSharpEditorUtility._programAssetLookup` (MonoScript -> asset). Both are built lazily once per editor session. If the `.asset` files were created **after** the caches were first built (e.g. by an `AssetPostprocessor`-based auto-generator running in the same session, as in this project's `Assets/Editor/UdonSharpProgramAssetAutoGenerator.cs`), the lookup returns null and setup NREs. The bytecode itself is usually fine - only the editor-side lookup is stale. A domain reload also fixes it, but that is a heavy hammer for an automation pass.

Fix: clear both caches via the internal `UdonSharpProgramAsset.ClearProgramAssetCache()` (it also calls `UdonSharpEditorUtility.ResetCaches()`) before creating components:

```csharp
var clear = typeof(UdonSharpProgramAsset).GetMethod("ClearProgramAssetCache",
    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
clear?.Invoke(null, null);
// now safe: go.AddUdonSharpComponent<T>() ...
```

Run this once at the top of any scene-builder/prefab-generator that may execute in the same session where new U# program assets were just auto-created. It is cheap and idempotent.

## 10. uint in Udon: no `%` operator, and `(int)uint` throws on large values

Two silent killers around `uint` hash math in Udon runtime code:

- **No uint/long remainder**: `h % 500u` and `h % 500L` both fail Udon compilation ("Method is not exposed to Udon"). Do modulo in `int` space only.
- **`(int)uintVar` compiles to `Convert.ToInt32(uint)`**, which *throws* `OverflowException` at runtime when the value exceeds `Int32.MaxValue` (Udon runtime exception, behaviour halted). Mask first: `(int)(h & 0x7FFFFFFFu)` is always safe.

Exact `h % 500u` (uint remainder) reconstructed with Udon-safe ops:

```csharp
uint h = NoiseHash(...);
int zi = (int)(h & 0x7FFFFFFFu);
if ((h & 0x80000000u) != 0u) zi += int.MinValue;   // exact bit reinterpret, no Convert call
int r = zi % 500;
if (zi < 0) r = (r + 796) % 500;                   // 796 = (2^32 mod 500=296) + 500; verified == h % 500u
```

(General form: for modulus m with k = 2^32 mod m, `if (zi < 0) r = (zi % m + m + k) % m`.)

## 11. Making a field `public` on an existing U# behaviour wipes its scene value

Changing `private readonly int[] table = {...}` to `public int[] table = {...}` compiles fine, but at runtime the Udon heap takes the field's default from the **scene-serialized backing UdonBehaviour**, which has no stored value for the newly-public field (arrays come back length 0, values 0). Generation code then dies on index access with no clear error. Fix after any private->public visibility change on a scene-placed behaviour: re-run `UdonSharpEditorUtility.CopyProxyToUdon()` for the affected components and save the scene.

## 12b. Auto-generated program assets need a manual `ScriptVersion` stamp before `CopyProxyToUdon`

Symptom: `CopyProxyToUdon` throws `InvalidOperationException: Cannot run serialization ... with outdated script version` for a **brand-new** script whose program asset was just created by `UdonSharpProgramAssetAutoGenerator`, even right after a successful `CompileSync()`.

Cause: the auto-generator uses `ScriptableObject.CreateInstance<UdonSharpProgramAsset>()`, which leaves `scriptVersion = UdonSharpProgramVersion.Unknown` (0). The normal UdonSharp upgrader pass that stamps new assets to `CurrentVersion` never runs for it, and `CompileSync()` only updates `CompiledVersion` (bytecode), not `ScriptVersion`. The gate `programAsset.ScriptVersion < UdonSharpProgramVersion.CurrentVersion` then fails forever.

Fix (safe for scripts written in current U# style): stamp the metadata directly once, then proceed:

```csharp
var pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Assets/Scripts/NewScript.asset");
pa.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
EditorUtility.SetDirty(pa);
AssetDatabase.SaveAssets();
```

## 12c. `SendCustomNetworkEvent` on the PROXY silently no-ops under MultiSim (use the backing UdonBehaviour)

Under VRChat MultiSim (ParrelSync two-editor setup), calling `proxy.SendCustomNetworkEvent(...)` from editor dynamic code on an UdonSharpBehaviour **silently drops the event** - nothing executes, no error. The same call on the backing `UdonBehaviour` works:

```csharp
var udon = proxy.GetComponent<VRC.Udon.UdonBehaviour>();
udon.SendCustomNetworkEvent(NetworkEventTarget.Owner, "RequestHarvestTile", tx, tz, toolType);
```

(Plain single-player ClientSim tolerates the proxy route - in-game code (Udon context) is unaffected either way. If an editor-driven network event "does nothing" in a MultiSim session, suspect this first.)

Related: when simulating a SECOND player's action from editor code, `SendCustomEvent` runs LOCALLY on that editor - owner-guarded methods (`if (!Networking.IsOwner) return;`) are silently rejected on non-owner clients. To exercise the real client->owner path, always use `udon.SendCustomNetworkEvent(NetworkEventTarget.Owner, ...)` from the client editor.

## 13. `BehaviourSyncMode.NoVariableSync` on the SENDER silently kills `SendCustomNetworkEvent` (real-client-only failure)

An UdonSharpBehaviour with `[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]` cannot SEND network events: `otherBehaviour.SendCustomNetworkEvent(...)` called from inside it silently never fires on the wire. Single-player ClientSim still works (local player owns everything, so events short-circuit), and MultiSim tests that fire events directly on backing UdonBehaviours bypass the real sender code path - so this ONLY shows up when a second REAL client interacts. Symptom: "remote player can't harvest / pull the lever / place rails, but the master can".

Fix: every behaviour whose code path can call `SendCustomNetworkEvent` must be `Manual` (no synced vars = zero bandwidth) or `Continuous`. The TARGET behaviour must also be registered (Manual+), but that is usually already true.

Audit after any NoVariableSync attribute: grep the class body for `SendCustomNetworkEvent` - if present, the attribute is a bug.

Testing lesson: MultiSim verification that fires events editor-side directly on backing UdonBehaviours does NOT exercise the real in-game sender behaviours (lever, tools, rail items). Always follow up with either a real second-client test or a path that invokes the actual sender component (e.g. `proxy.Interact()` / pickup events).

## 14. Held VRCPickup vs kinematic/static world colliders: `OnCollisionStay` is unreliable for tools

A held VRCPickup's rigidbody is kinematic on the holder's client, and Unity generates NO collision callbacks for kinematic-vs-kinematic (or kinematic-vs-static) pairs - so a "swing the axe at the tree" `OnCollisionStay` harvest only fires when the tool is UNHELD and resting against the node (dynamic). It appears to "work for the master, sometimes fail for remote" because fast swings also tunnel through small colliders. Reliable tool-hit pattern (ToolBehaviour in this project):

1. Primary: `OnPickupUseDown` + `Physics.OverlapSphere` at the tool head (VR swing) falling back to a raycast from `TrackingDataType.Head` along look direction (desktop aim). Convert hit to a tile and send `SendCustomNetworkEvent(Owner, ...)`; the owner validates type/tool/mask, so client-side precision is not critical.
2. Secondary: keep `OnCollisionStay`/`OnCollisionEnter` for unheld resting tools (fun auto-harvest).
3. `rigidbody.collisionDetectionMode = ContinuousDynamic` on the tool to reduce tunneling.

## 15. ParrelSync clone does not notice host-written asset changes until `AssetDatabase.Refresh()`

Host-side scene/asset saves travel through the ParrelSync junction on disk, but the clone editor's AssetDatabase keeps its cached view: `OpenScene` then loads the STALE scene (symptom: newly wired reference fields read null on the clone while the arrays/program are new). On the clone, always `AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport)` before reopening the scene after host-side scene edits.

## 12. Play-mode test state: write via `SetProgramVariable`, never via proxy fields or proxy method calls

In play mode, public-field storage for Udon programs lives in the **backing UdonBehaviour's program variables**, not in the proxy's managed fields (extends #7). Consequences observed in ClientSim:

- `proxy.field = x` from `execute-dynamic-code` is **invisible to Udon from the first instant** (`GetProgramVariable` still returns the old value), and the proxy is refreshed from the backing on a cadence, so the write "reverts" within ~1-3s on reads too.
- Calling proxy methods with side effects (e.g. `gm.OwnerDepositResource(...)`) runs the C# against proxy storage: the logic outcome is observable inline (valid as a logic test), but synced/int state changes do **not** persist - they revert on the next proxy refresh, and `RequestSerialization()` called from such a context serializes the unchanged heap values. Pure-Unity side effects (pool SetActive etc.) do persist, which makes the divergence extra confusing.
- Udon-context changes persist: `udon.SendCustomEvent(...)`, real gameplay events, physics callbacks (OnTriggerStay deposits). Verified stable for 20s+.

Correct editor-script recipes:

```csharp
var udon = proxy.GetComponent<VRC.Udon.UdonBehaviour>();
udon.SetProgramVariable("woodCount", 7);        // setup test state - persists
object v = udon.GetProgramVariable("woodCount"); // read runtime state
udon.SendCustomEvent("RequestWithdrawRail");     // drive logic (custom methods keep names, see #3)
```
