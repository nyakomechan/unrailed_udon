#if UNITY_EDITOR
using System;
using UnityEngine;
using VRC.SDK3.ClientSim.Interfaces;
using VRC.SDK3.Data;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;

namespace MultiSim
{
    /// <summary>
    /// Replacement for ClientSim's UdonBehaviour encoder/decoder.
    ///
    /// ClientSim's ClientSimUdonEncodeDecode resolves variable types via
    /// UdonBehaviour.GetProgramVariableType, which reflects the *current boxed value* on the
    /// Udon heap. Its object-typed SetProgramVariable writes then retype the heap slot, after
    /// which the type lookup returns System.Object, the decoder's type switch falls through to
    /// null, and every subsequent sync erases the synced variables (breaking UdonSharp reads).
    ///
    /// This codec instead uses the symbol table's *declared* type and writes through
    /// IUdonHeap.SetHeapVariable(address, value, declaredType), keeping the slot strongly typed.
    /// Values travel as MultiSimJson typed tokens so the exact .NET type survives JSON.
    ///
    /// Writing straight to the heap skips UdonBehaviour's [FieldChangeCallback] dispatch, which
    /// only runs from its private SetHeapVariable&lt;T&gt;, so this codec reproduces that dispatch
    /// itself: change-gate, write the previous value to the "_old_" symbol, then run the
    /// "_onVarChange_" entry point.
    /// </summary>
    internal class MultiSimUdonCodec : IClientSimEncodeDecoder
    {
        /// <summary>
        /// Behaviours whose decode landed before Start, so OnDeserialization (and any field
        /// change callbacks) must wait until the Udon VM is up. Flushed from MultiSimCore.LateUpdate.
        /// </summary>
        private static readonly System.Collections.Generic.List<DeferredDecode> Deferred =
            new System.Collections.Generic.List<DeferredDecode>();

        private class DeferredDecode
        {
            public UdonBehaviour Behaviour;
            public readonly System.Collections.Generic.List<string> ChangeEvents =
                new System.Collections.Generic.List<string>();
        }

        public void PreEncode(MonoBehaviour component)
        {
            ((UdonBehaviour)component).OnPreSerialization();
        }

        public DataDictionary Encode(MonoBehaviour component)
        {
            UdonBehaviour udonBehaviour = (UdonBehaviour)component;
            DataDictionary data = new DataDictionary();

            foreach (IUdonSyncMetadata metadata in GetSyncMetadata(udonBehaviour))
            {
                object value = udonBehaviour.GetProgramVariable(metadata.Name);
                data[metadata.Name] = MultiSimJson.ObjectToTypedToken(value);
            }

            return data;
        }

        public void PostEncode(MonoBehaviour component, DataDictionary data)
        {
            ((UdonBehaviour)component).OnPostSerialization(new SerializationResult(true, data.Count * 4));
        }

        public bool IsManualSynced(MonoBehaviour component)
        {
            return ((UdonBehaviour)component).SyncMethod == VRC.SDKBase.Networking.SyncType.Manual;
        }

        public bool IsDirty(MonoBehaviour component, DataDictionary data)
        {
            UdonBehaviour udonBehaviour = (UdonBehaviour)component;

            foreach (IUdonSyncMetadata metadata in GetSyncMetadata(udonBehaviour))
            {
                if (!data.TryGetValue(metadata.Name, out DataToken previous))
                {
                    return true;
                }

                DataToken current = MultiSimJson.ObjectToTypedToken(udonBehaviour.GetProgramVariable(metadata.Name));
                if (!TokensEqual(previous, current))
                {
                    return true;
                }
            }

            return false;
        }

        public void Decode(MonoBehaviour component, DataDictionary data)
        {
            UdonBehaviour udonBehaviour = (UdonBehaviour)component;
            IUdonProgram program = MultiSimReflect.GetUdonProgram(udonBehaviour);
            if (program == null)
            {
                return;
            }

            IUdonSymbolTable symbolTable = program.SymbolTable;
            IUdonHeap heap = program.Heap;
            // Before Start the Udon VM is not up yet, so callbacks (and OnDeserialization) wait.
            DeferredDecode deferred = udonBehaviour.HasDoneStart ? null : GetOrCreateDeferred(udonBehaviour);

            foreach (IUdonSyncMetadata metadata in GetSyncMetadata(udonBehaviour))
            {
                if (!data.TryGetValue(metadata.Name, out DataToken token))
                {
                    continue;
                }

                if (!symbolTable.TryGetAddressFromSymbol(metadata.Name, out uint address))
                {
                    continue;
                }

                Type declaredType = symbolTable.GetSymbolType(metadata.Name);
                object value = MultiSimJson.TypedTokenToObject(token);

                if (value == null)
                {
                    if (declaredType.IsValueType)
                    {
                        // Never null out a value-type slot; that corrupts the UdonSharp heap.
                        continue;
                    }
                }
                else if (!declaredType.IsInstanceOfType(value))
                {
                    try
                    {
                        value = Convert.ChangeType(value, declaredType);
                    }
                    catch (Exception)
                    {
                        MultiSimLog.Warn($"Cannot convert synced value of '{metadata.Name}' " +
                                         $"({value.GetType().Name} -> {declaredType.Name}) on '{udonBehaviour.name}'.");
                        continue;
                    }
                }

                object previous = heap.GetHeapVariable(address);
                // Same change gate as UdonBehaviour.SetHeapVariable. The write itself always
                // happens so the slot keeps its declared type even on an unchanged value.
                bool changed = !(previous?.Equals(value) ?? value == null);

                heap.SetHeapVariable(address, value, declaredType);

                if (!changed)
                {
                    continue;
                }

                string changeEvent = VariableChangedEvent.EVENT_PREFIX + metadata.Name;
                if (!program.EntryPoints.HasExportedSymbol(changeEvent))
                {
                    continue;
                }

                // The generated setter reads the field for its "value" parameter and restores
                // the field from "_old_", so both have to be in place before the event runs.
                string oldSymbol = VariableChangedEvent.OLD_VALUE_PREFIX + metadata.Name;
                if ((previous != null || !declaredType.IsValueType) &&
                    symbolTable.TryGetAddressFromSymbol(oldSymbol, out uint oldAddress))
                {
                    // UdonBehaviour writes this slot untyped; keep the declared type instead so
                    // the heap stays strongly typed (the whole point of this codec).
                    heap.SetHeapVariable(oldAddress, previous, symbolTable.GetSymbolType(oldSymbol));
                }

                if (deferred != null)
                {
                    if (!deferred.ChangeEvents.Contains(changeEvent))
                    {
                        deferred.ChangeEvents.Add(changeEvent);
                    }
                    continue;
                }

                udonBehaviour.RunProgram(changeEvent);
                if (udonBehaviour == null)
                {
                    // A callback can destroy its own behaviour.
                    return;
                }
            }

            if (deferred != null)
            {
                return;
            }

            udonBehaviour.OnDeserialization(new DeserializationResult(0, 0, true));
        }

        /// <summary>
        /// Runs the deserialization work that was held back because the behaviour had not
        /// started yet. Called once per frame from MultiSimCore.LateUpdate.
        /// </summary>
        public static void FlushDeferred()
        {
            for (int i = Deferred.Count - 1; i >= 0; i--)
            {
                DeferredDecode entry = Deferred[i];
                if (entry.Behaviour == null)
                {
                    Deferred.RemoveAt(i);
                    continue;
                }

                if (!entry.Behaviour.HasDoneStart)
                {
                    continue;
                }

                Deferred.RemoveAt(i);

                foreach (string changeEvent in entry.ChangeEvents)
                {
                    entry.Behaviour.RunProgram(changeEvent);
                    if (entry.Behaviour == null)
                    {
                        break;
                    }
                }

                if (entry.Behaviour != null)
                {
                    entry.Behaviour.OnDeserialization(new DeserializationResult(0, 0, true));
                }
            }
        }

        public static void ClearDeferred()
        {
            Deferred.Clear();
        }

        private static DeferredDecode GetOrCreateDeferred(UdonBehaviour udonBehaviour)
        {
            foreach (DeferredDecode entry in Deferred)
            {
                if (entry.Behaviour == udonBehaviour)
                {
                    return entry;
                }
            }

            DeferredDecode created = new DeferredDecode { Behaviour = udonBehaviour };
            Deferred.Add(created);
            return created;
        }

        private static System.Collections.Generic.IEnumerable<IUdonSyncMetadata> GetSyncMetadata(
            UdonBehaviour udonBehaviour)
        {
            IUdonSyncMetadataTable table = udonBehaviour.SyncMetadataTable;
            return table != null
                ? table.GetAllSyncMetadata()
                : Array.Empty<IUdonSyncMetadata>();
        }

        private static bool TokensEqual(DataToken a, DataToken b)
        {
            if (a.TokenType != b.TokenType)
            {
                return false;
            }

            switch (a.TokenType)
            {
                case TokenType.DataList:
                {
                    DataList listA = a.DataList;
                    DataList listB = b.DataList;
                    if (listA.Count != listB.Count)
                    {
                        return false;
                    }
                    for (int i = 0; i < listA.Count; i++)
                    {
                        if (!TokensEqual(listA[i], listB[i]))
                        {
                            return false;
                        }
                    }
                    return true;
                }
                case TokenType.DataDictionary:
                {
                    DataDictionary dictA = a.DataDictionary;
                    DataDictionary dictB = b.DataDictionary;
                    if (dictA.Count != dictB.Count)
                    {
                        return false;
                    }
                    foreach (System.Collections.Generic.KeyValuePair<DataToken, DataToken> pair in dictA)
                    {
                        if (!dictB.TryGetValue(pair.Key, out DataToken other) || !TokensEqual(pair.Value, other))
                        {
                            return false;
                        }
                    }
                    return true;
                }
                default:
                    return a.Equals(b);
            }
        }
    }
}
#endif
