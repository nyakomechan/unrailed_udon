# VRChat MultiSim — 開発引き継ぎドキュメント

このドキュメントは、開発を引き継ぐ人間/AIエージェント向けの技術資料です。
ユーザー向けの使い方は [README.md](README.md) を参照。

- **対象環境**: Unity 2022.3.22f1 / com.vrchat.worlds **3.10.4**(VCC) / ParrelSync(git URL導入)
- **開発プロジェクト**: `F:/Unity_projects_2019/test26c`(オリジナル=ホスト)
- **クローン**: `F:/Unity_projects_2019/test26c_clone_0`(ParrelSyncで作成済み。AssetsはJunction共有なのでコード修正は自動で両方に反映される)
- **検証状況**: 2026-07-06 に2エディタ実機テスト済み。参加/退出、同期変数(Manual)、引数付きネットワークイベント、Ownership移譲、プレイヤー位置同期、レイトジョインスナップショットの全経路が動作確認済み。エラー0。
- **パッケージ化**: `Packages/com.nyakomechan.vrchat-multisim/` の埋め込みVPM/UPMパッケージ
  (`package.json` あり、UPM git URL `?path=/Packages/com.nyakomechan.vrchat-multisim` で導入可)。
  ParrelSyncはVPM依存にできないため、asmdefの `versionDefines`(`MULTISIM_PARRELSYNC`)+
  `MultiSimPrefs.IsCloneInstance()/CloneArgument()` のガードで**オプション依存**にしてある
  (未導入時はコンパイルが通り、常にホストとして動作)。
  ※ パッケージ移設後、2026-07-07 の Station 同期テスト(下記)で2エディタ動作を再検証済み。
- **Station同期 (v0.2.0)**: 2026-07-07 に2エディタ実機テスト済み。着席/退席イベント(双方向)、
  リモートプレイヤーの座席ピン留め(誤差0.000)、占有状態、レイトジョイン時の着席再現、全て動作。

---

## 1. 全体アーキテクチャ

```
[オリジナルEditor] = ホスト(player 1, master)     [クローンEditor] = クライアント(player 2..)
        │  TcpListener 127.0.0.1:24685                     │  TcpClient
        └────────── 長さプレフィックス(4byte LE) + UTF8 JSON ──────────┘
                     ホストが全メッセージを他クライアントへ中継(リレー)
```

役割は `ParrelSync.ClonesManager.IsClone()` で自動判別。クローン=クライアント。

### 起動シーケンス(最重要)

ClientSim は `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` で自動起動し、通常は数フレームで
「Ready」(Udon開始 + `OnPlayerJoined` 配信)になる。MultiSim はこの **Ready だけを保留** し、
ネットワークハンドシェイクでプレイヤーIDが確定してから Ready 処理を自前で再現する。

```
[SubsystemRegistration] MultiSimCore.EarlyInit
    ClientSimSettings をEditorPrefsから読み、
    initializationDelay = +∞ / localPlayerIsMaster = true に改変して
    ClientSimSettings._instance へリフレクション注入
        ↓
[BeforeSceneLoad] ClientSim が通常起動(全SDKフック設置、ローカルプレイヤー spawn, id=1)
    ※ ready コルーチンは WaitForSeconds(∞) で永久待機 ← これが「保留」の実体
        ↓
[AfterSceneLoad] MultiSimCore.LateInit → __MultiSim GameObject 生成
        ↓
Awake: コーデック差し替え / フック設置 / TCP開始
    ホスト: 1フレーム後 registry構築 → ReadySequence() 即実行 → HELLO待受
    クライアント: 接続 → HELLO送信 → WELCOME受信 → プレイヤーID fixup → ReadySequence()
                  → バッファ済みメッセージ(snapshot含む)適用
        ↓
ReadySequence() = ClientSimMain.InitializeClientSim の後半を忠実に再現:
    EnablePlayer → _isReady=true → udonManager.OnClientSimReady()(Udon start発火)
    → playerManager.OnClientSimReady()(OnPlayerJoined一括配信)
    → ClientSimReadyEvent → stackedCamera.Ready()
```

ハンドシェイク失敗時(ホスト不在15秒/ポートbind失敗)は警告を出して ReadySequence を実行し、
**通常のシングルプレイClientSimにフォールバック**する(絶対にハングさせない)。
`ReleaseClientSimHold()` は初期化自体が失敗した場合に delay=0 へ戻す安全弁。

### プレイヤーID管理

- VRChat実機同様、**全インスタンスでIDが一致することが最重要**(Udonコードが playerId を同期変数に入れるため)。
- ホストが採番(host=1固定, クライアントは接続順に2,3,...)。master は常に host(=1)。
- クライアントは WELCOME 受信時に fixup を実行(`HandleWelcome`):
  1. ローカルプレイヤー(仮ID 1)を割当IDへ再マップ — `MultiSimReflect.RemapPlayerId` が
     `ClientSimPlayerManager._playerIDs/_players/_localPlayerID` と `VRCPlayerApi.mPlayerId`(キャッシュ)を書き換え
  2. 既存プレイヤーを参加順に `ClientSimMain.SpawnRemotePlayer()`(直前に `_nextPlayerID` を強制セットしてIDを制御)
  3. `_masterID = 1`、`VRCPlayerApi.AllPlayers` を参加順にソート(master退出時のClientSim内部assertが AllPlayers[0]==master を要求するため)
- `localPlayerIsMaster=true` を強制するのは「ローカルが最初にID 1でspawnする」前提をfixupが置くため。

---

## 2. ファイル構成と責務

| ファイル | 責務 |
|---|---|
| `MultiSimCore.cs` | 中心。起動フック(EarlyInit/LateInit)、ハンドシェイク、ID fixup、ReadySequence、Update同期ループ、全メッセージのハンドラ、SDKフック設置/解除 |
| `MultiSimTransport.cs` | TCP。受信スレッド→`ConcurrentQueue`→メインスレッド`Poll`。`ConnectToHostAsync`は失敗時 ConnectionId=-1 の Disconnected イベントを流す |
| `MultiSimReflect.cs` | ClientSim内部へのリフレクションを**全て集約**。`InitializeOrThrow()` が起動時に全メンバーを検証し、SDK非互換なら「どのメンバーが無いか」を例外メッセージで報告 |
| `MultiSimRegistry.cs` | NetworkID → `ClientSimNetworkIdHolder` のマップ。holder が無ければ AddComponent して view に接続 |
| `MultiSimUdonCodec.cs` | UdonBehaviour用の自前Encode/Decode(§4.1のClientSimバグ回避)。`IClientSimEncodeDecoder` 実装 |
| `MultiSimJson.cs` | VRCJsonラッパー + イベント引数用の型タグ付きトークン(`{"t":"int","v":1}` 形式) |
| `MultiSimPrefs.cs` | EditorPrefs設定(`MultiSim.Enabled` / `MultiSim.Port`=24685 / `MultiSim.Verbose`)。**EditorPrefsはクローンと共有される**(意図的) |
| `MultiSimMenu.cs` | Tools/VRChat MultiSim メニュー |
| `MultiSimLog.cs` | `[MultiSim]` プレフィックスログ。Verbose は Prefs でゲート |
| `Samples~/MultiSimTestBoard/MultiSimTestBoard.cs` | 検証用U#ギミック(Manual sync + [NetworkCallable]イベント)。Samples経由でAssetsへImportして使う(U#はAssembly-CSharp必須のため**パッケージ内では動かない**)。開発プロジェクトには untracked コピーが `Assets/MultiSimSamples/` にあり、テストシーンはそれを参照 |

asmdef `VRChat.MultiSim`: **全プラットフォーム**(includePlatforms空)+ 全ファイル `#if UNITY_EDITOR` 囲い。
references: VRC.ClientSim / VRC.Udon / VRC.SDKBase / ParrelSync(欠落時は無視される)。
versionDefines で ParrelSync 導入時のみ `MULTISIM_PARRELSYNC` が定義される。

---

## 3. ワイヤープロトコル

エンベロープ: `{"t":<type>,"s":<senderPlayerId>,"p":{...}}`(VRCJson Minify、4byte LE長 + UTF8)

| type | 方向 | payload | 説明 |
|---|---|---|---|
| `hello` | C→H | `{name, ver}` | 接続直後。ver=`MultiSimPrefs.ProtocolVersion` |
| `welcome` | H→C(個別) | `{id, master, players:[{id,name,pos,rot}]}` | ID割当。players は参加順(自分を除く) |
| `join` | H→他C | `{id, name}` | 新規参加の通知 |
| `leave` | H→全C | `{id}` | 切断検知時 |
| `snapshot` | H→C(個別) | `{objs:[{nid, owner, data?}], seats:[{nid, pid}]}` | レイトジョイン用。ホスト所有物は新規Encode、他人の物は最後に受信した `holder.GetData()`。seats は現在の着席状態 |
| `var` | 所有者→全員 | `{nid, data:DataList}` | 同期変数。data は holder.Encode の出力そのまま |
| `owner` | 実行者→全員 | `{nid, owner}` | Ownership変更 |
| `event` | 送信者→全員 | `{nid, ub, name, tgt, prm:[typedToken]}` | ub=-1 はレガシーイベント(GO上の全syncable UBへファンアウト)。tgt=NetworkEventTarget(int) |
| `pos` | 各自→全員 | `{pos, rot}` | プレイヤー位置(10Hz、移動時のみ)。対象プレイヤーは s から特定。着席中は送信停止 |
| `sit` | 着席者→全員 | `{nid}` | Station着席。s のプレイヤーが nid のStationに座った |
| `unsit` | 退席者→全員 | `{nid}` | Station退席 |

- **リレー**: ホストは hello 以外の全クライアント発メッセージを「そのままのJSON文字列で」他クライアントへ再送→自分にも適用。TCP+単一リレーなので順序は全員で一致する。
- **クライアントの受信規約**: 最初に受信するのは必ず welcome(ホストは接続ごとに welcome を最初に書き込むため)。Ready前に届いた var/owner/event/pos/join/leave/snapshot は `_pendingWorldMessages` にバッファし、ReadySequence 完了後に到着順で適用(snapshot が自然に先頭になる)。
- **受信側のターゲット解釈**: `Others`=送信者は自分でローカル実行済み&自分には届かないので受信者は常に実行 / `Owner`=ローカルが所有している場合のみ実行 / `All`=実行。
- **JSON経由の数値は全て Double トークン**になる(VRCJsonの仕様)。読む側は `(int)token.Double` の形にすること。

---

## 4. 調査で判明した落とし穴(再修正・改造時は必読)

### 4.1 ClientSim標準UdonコーデックはU#ヒープを破壊する【最重要】
`ClientSimUdonEncodeDecode.Decode` は `UdonBehaviour.GetProgramVariableType()` で型を引くが、
これは **ヒープ上の現在値のボックス型** を返す。一方 ClientSim の `SetProgramVariable(string, object)`
経由の書き込みはスロットを object 型化してしまい、次回から型が `System.Object` になる
→ 型switchが default に落ちて **null を書き込み、以降その同期変数は壊れる**(U#の
`$"{_field}"` が空文字になる、実測済み)。

対策として `MultiSimUdonCodec` は:
- 型は `program.SymbolTable.GetSymbolType(name)`(**宣言型**)で取得
- 書き込みは `IUdonHeap.SetHeapVariable(address, value, declaredType)`(型指定オーバーロード)
- null は値型スロットには書かない
- 値は MultiSimJson の型タグ付きトークンで運ぶ(JSON往復で正確な.NET型を復元)

**代償: FieldChangeCallback は自前で発火する必要がある。**
`[FieldChangeCallback]` の実体は U#コンパイラが吐く `_onVarChange_<フィールド名>` エントリポイントで、
`UdonBehaviour` はこれを private `SetHeapVariable<T>` からのみ実行する
(値が変化したときだけ、新値をフィールドに、旧値を `_old_<フィールド名>` に書いてから実行)。
ヒープへ直接書く上記の方式ではこの経路を通らないため、`MultiSimUdonCodec.Decode` が同じ手順を再現している:
変化判定 → 型指定書き込み → `_old_` を**宣言型で**書く(本家は object 型化しうる) → `RunProgram(changeEvent)`。
Start前に届いた分は `_old_` まで書いて発火だけ保留し、`FlushDeferred()`(MultiSimCore.LateUpdate)で
`OnDeserialization` の直前に実行する(UdonVM未初期化だと `RunProgram` が黙って捨てられるため)。

差し替えは `ClientSimNetworkIdHolder.encodeDecoders`(internal static Dictionary)を
リフレクションで書き換え(`MultiSimReflect.SwapEncodeDecoder`)、OnDestroyで復元。
**ClientSim標準コーデックに戻すと再発する。** VRCObjectSync / VRCObjectPool のコーデックは
C#プロパティ操作なのでこの問題は無く、ClientSim標準のまま。

### 4.2 ClientSimNetworkingView は FindObjectsByType に掛からない
`ClientSimBehaviour.Awake` が `DontSaveInEditor|DontSaveInBuild` hideFlags を付けるため、
`Object.FindObjectsByType` では **0件** になる(GetComponent系は問題なし)。
ネットワークオブジェクトの列挙は `VRC_SceneDescriptor.Instance.NetworkIDCollection` 経由で行うこと
(`MultiSimRegistry.BuildFromScene` 参照)。

### 4.3 ConfigureNetworkOnScene は BeforeSceneLoad では機能しない
ClientSim は起動時(BeforeSceneLoad)に `ClientSimNetworkingUtilities.ConfigureNetworkOnScene` を
呼ぶが、エディタPlayではこのタイミングで view が付かない(実測: シーン内 view 0件)。
`MultiSimRegistry.BuildFromScene` が Play開始1フレーム後に**再実行**している(冪等: IDは
NetworkIDCollection 由来なので安全)。NetworkID は Transform パス順で決定的に割り当てられるため
クローン間で必ず一致する。

### 4.4 Editor専用asmdefのMonoBehaviourはAddComponentできない
`includePlatforms:["Editor"]` のasmdefに入れたMonoBehaviourは
"Can't add script behaviour ... because it is an editor script"(**例外ではなくnull返し+ログ**)で失敗する。
現在の「全プラットフォームasmdef + `#if UNITY_EDITOR`」方式は ClientSim 自身と同じパターン。
新ファイル追加時も必ず `#if UNITY_EDITOR` で囲むこと。

### 4.5 その他
- `VRCPlayerApi.playerId` は private `mPlayerId` にキャッシュされる。ID再マップ時は辞書と両方更新必須。
- `NetworkCalling.SendCustomNetworkEventProxy` はデリゲート**プロパティ**で、型(`SendNetworkEventDelegate`)は
  VRCSDK3内部。購読は `Delegate.CreateDelegate` + `Delegate.Combine`(`MultiSimReflect.AddSendCustomNetworkEventHandler`)。
- `NetworkCalling.WithNetworkCallingContext` は internal。代わりに public な
  `InNetworkCall` / `CallingPlayer` setter を直接使う(受信イベント実行時に送信者をセット→finallyで解除)。
- 受信イベントのローカル実行は `RunEventAdvanced<object,...>(name, mangleParameterNames:false,
  canRunBeforeStart:true, (metadata.Parameters[i].Name, value)...)`。ClientSim実装のコピー。
- ループ防止はフラグではなく**経路設計**: 受信適用は `ClientSimPlayerManager.SetOwner` 直呼び /
  `RunEventAdvanced` 直呼びなので、送信フック(`Networking._SetOwner` / proxyデリゲート)を通らない。
  唯一のフラグは `_applyingRemoteOwner`。
- ClientSimSettings は EditorPrefs(キー `com.vrchat.clientsim.settings`)から読まれ、
  MultiSim が `_instance` を実行時オブジェクトで差し替える。Play終了時に必ず null へ戻す
  (`playModeStateChanged` + OnDestroy)。戻し忘れると **initializationDelay=∞ が
  ClientSim設定ウィンドウ経由で永続化される事故**につながる。
- Manual sync の送信は `UdonBehaviour.RequestSerializationHook` → キュー → LateUpdate flush
  (VRChatの「フレーム末尾に直列化」を再現)。ContinuousはUpdateで0.1s毎に `IsDirty(null)` ポーリング
  (manual holder は `IsDirty(null)`=false になる仕様を利用)。
- `holder.Encode(gameObject)`(引数=自分のGO)は manual ガードをバイパスして強制Encodeできる
  (スナップショットとmanual送信で使用)。

### Station同期の仕組み(v0.2.0)
- ローカルの着席/退席は ClientSim のディスパッチャイベント
  `ClientSimOnPlayerEnteredStationEvent` / `ClientSimOnPlayerExitedStationEvent` を購読して検知
  (ClientSimPlayerStationManager がローカルプレイヤーの着席時に必ず発行する)。
- Station の特定は NetworkID(= VRCStation と同じGOにネットワークコンポーネントが必要)。
  レジストリの `TryGetGameObject` は NetworkIDCollection ベースの全ネットワークGOマップを使う
  (holder マップは同期変数のあるGOのみなので不十分)。
- 受信側: リモートプレイヤーを `stationEnterPlayerLocation` に毎LateUpdateピン留め
  (動くStation対応)。退席時は `stationExitPlayerLocation` へワープ。着席中は `pos` を無視。
- Udonイベント発火: `VRCStation.OnRemotePlayerEnterStation/ExitStation` フィールドのイベント名
  (U#が `_onStationEntered/_onStationExited` を設定する。空ならこの名前にフォールバック)を
  GO上の全UdonBehaviourへ `RunEvent(name, ("Player", api))` で実行。
  ローカル側の `OnLocalPlayerEnterStation` は ClientSim(ClientSimUdonHelper)が発火済み。
- 占有状態: `ClientSimStationHelper._usingPlayer` をリフレクションでセット
  (helperのEnterStationはリモートプレイヤーを拒否するため)。
- 制限: 着席ポーズなし(立ち姿でピン留め)、退席は移動入力/テレポート/Udonの
  ExitStation経由(ClientSimの挙動そのまま)。プレイヤー退出時は自動で退席+イベント発火。

---

## 5. 検証手順(uloopMCP使用)

両エディタが起動していれば、CLIから自動テストできる:

```bash
# ホスト(test26c がカレント)
npx --yes uloop-cli@2.2.0 control-play-mode --action Play
# クローン
npx --yes uloop-cli@2.2.0 control-play-mode --action Play --project-path F:/Unity_projects_2019/test26c_clone_0

# ハンドシェイク確認(両側で "Ready. Role: HOST/CLIENT, local player id: 1/2")
npx --yes uloop-cli@2.2.0 get-logs --search-text "Ready. Role"

# ホスト側でInteract(execute-dynamic-code)
#   GameObject.Find("MultiSimTestBoard").GetComponent<UdonBehaviour>().Interact();
# → クローン側ログに OnDeserialization: count=N と NetworkEvent received が出れば成功
npx --yes uloop-cli@2.2.0 get-logs --search-text "MultiSimTestBoard" --project-path <クローン>
```

期待されるログ系列(クローン側): `OnPlayerJoined(host, master=True)` → `OnPlayerJoined(self)` →
snapshot の `OnDeserialization` → 以降プレスごとに `NetworkEvent received` + `OnDeserialization`。
デバッグ時は `Tools/VRChat MultiSim/Verbose Logging` をONにすると全送受信メッセージ
(`send 'var' (...): {json}` / `recv 'var' from 1`)が出る。

シーン: `VRCDefaultWorldScene` に検証用Cube「MultiSimTestBoard」設置済み(Manual sync)。

---

## 6. 未対応・既知の制限(=今後のタスク候補)

- **補間なし**: pos / VRCObjectSync は10Hzの直接セット。リモートプレイヤーとオブジェクトがカクつく。
  → 受信側でLerp/外挿を挟むのが次の改善候補(HandlePlayerPosition / ObjectSyncコーデック)
- **リモートプレイヤーの姿勢**: ルートのpos/rotのみ。頭・手のボーンは未同期
  (送る場合は `pos` メッセージに TrackingData を追加し、リモートアバターのボーンへ適用)
- **ボイス / アバター / PlayerData・Persistence** 未対応
  (PersistenceはSDK 3.10.4では `VRC_ENABLE_PLAYER_PERSISTENCE` 定義自体が無効)
- **Station**: 着席ポーズ・アニメーションは未再現(位置固定のみ)。VRCStation単独GO
  (ネットワークコンポーネントなし)は同期不可
- **VRCInstantiate オブジェクト**はNetworkIDが無いため同期不可(実機仕様と同じ。ObjectPool推奨)
- **master移譲**: ホスト退出=セッション終了扱い。3人以上でのmaster移譲順序は
  `AllPlayers[1]` 依存で厳密には未検証
- **Continuousの OnPreSerialization 内での変数変更**は1tick遅れる(IsDirty→Encodeの順のため)
- **char型の同期変数**はVRCJson経由の直列化が未検証(型タグでstring化しているので恐らく動くが要確認)
- ObjectPool の実機同期挙動(spawn/return の細部)は2エディタでの実測未実施
- ポート設定UIなし(EditorPrefs `MultiSim.Port` 直編集)

## 7. SDKバージョン更新時の対応手順

1. `MultiSimReflect.InitializeOrThrow()` が例外を出す → メッセージに欠落メンバー名が出る
2. `Packages/com.vrchat.worlds/Integrations/ClientSim/` の該当ソースを読み、リネーム/構造変更を確認
3. 特に注意: `ClientSimMain.InitializeClientSim` の後半(ReadySequenceが再現している部分)が
   変わっていないか。変わっていたら `MultiSimCore.ReadySequence()` を追従させる
4. `ClientSimNetworkIdHolder` / エンコーダ群のインターフェース変更は `MultiSimUdonCodec` に影響
