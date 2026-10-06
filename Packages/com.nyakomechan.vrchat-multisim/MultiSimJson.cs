#if UNITY_EDITOR
using System;
using UnityEngine;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace MultiSim
{
    /// <summary>
    /// Serialization helpers built on top of VRChat's DataToken/VRCJson.
    /// Message payloads reuse ClientSim's own Vector3/Quaternion token helpers so the
    /// wire format matches what ClientSimNetworkIdHolder.Encode() produces.
    /// Network-event parameters are wrapped with an explicit type tag so the exact
    /// .NET type can be reconstructed on the receiving side.
    /// </summary>
    internal static class MultiSimJson
    {
        public static string Serialize(DataDictionary dict)
        {
            if (VRCJson.TrySerializeToJson(dict, JsonExportType.Minify, out DataToken result))
            {
                return result.String;
            }

            MultiSimLog.Error($"Failed to serialize message to JSON: {result}");
            return null;
        }

        public static bool TryDeserialize(string json, out DataDictionary dict)
        {
            dict = null;
            if (!VRCJson.TryDeserializeFromJson(json, out DataToken token))
            {
                return false;
            }

            if (token.TokenType != TokenType.DataDictionary)
            {
                return false;
            }

            dict = token.DataDictionary;
            return true;
        }

        #region Typed object <-> token (for network event parameters)

        public static DataToken ObjectToTypedToken(object obj)
        {
            DataDictionary wrapper = new DataDictionary();
            if (obj == null)
            {
                wrapper["t"] = "null";
                return wrapper;
            }

            Type type = obj.GetType();
            if (type.IsArray)
            {
                wrapper["t"] = "arr:" + TypeTag(type.GetElementType());
                DataList list = new DataList();
                foreach (object element in (Array)obj)
                {
                    list.Add(ObjectToTypedToken(element));
                }
                wrapper["v"] = list;
                return wrapper;
            }

            wrapper["t"] = TypeTag(type);
            wrapper["v"] = ValueToToken(obj);
            return wrapper;
        }

        public static object TypedTokenToObject(DataToken token)
        {
            if (token.TokenType != TokenType.DataDictionary)
            {
                return null;
            }

            DataDictionary wrapper = token.DataDictionary;
            string tag = wrapper["t"].String;
            if (tag == "null")
            {
                return null;
            }

            if (tag.StartsWith("arr:", StringComparison.Ordinal))
            {
                string elementTag = tag.Substring(4);
                Type elementType = TypeFromTag(elementTag);
                if (elementType == null)
                {
                    return null;
                }

                DataList list = wrapper["v"].DataList;
                Array array = Array.CreateInstance(elementType, list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    array.SetValue(TypedTokenToObject(list[i]), i);
                }
                return array;
            }

            Type type = TypeFromTag(tag);
            if (type == null)
            {
                return null;
            }

            return TokenToValue(type, wrapper["v"]);
        }

        private static string TypeTag(Type type)
        {
            if (type == typeof(bool)) return "bool";
            if (type == typeof(char)) return "char";
            if (type == typeof(byte)) return "byte";
            if (type == typeof(sbyte)) return "sbyte";
            if (type == typeof(short)) return "short";
            if (type == typeof(ushort)) return "ushort";
            if (type == typeof(int)) return "int";
            if (type == typeof(uint)) return "uint";
            if (type == typeof(long)) return "long";
            if (type == typeof(ulong)) return "ulong";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(string)) return "string";
            if (type == typeof(Vector2)) return "v2";
            if (type == typeof(Vector3)) return "v3";
            if (type == typeof(Vector4)) return "v4";
            if (type == typeof(Quaternion)) return "q";
            if (type == typeof(Color)) return "color";
            if (type == typeof(Color32)) return "color32";
            if (type == typeof(VRCUrl)) return "url";
            return "unsupported:" + type.FullName;
        }

        private static Type TypeFromTag(string tag)
        {
            switch (tag)
            {
                case "bool": return typeof(bool);
                case "char": return typeof(char);
                case "byte": return typeof(byte);
                case "sbyte": return typeof(sbyte);
                case "short": return typeof(short);
                case "ushort": return typeof(ushort);
                case "int": return typeof(int);
                case "uint": return typeof(uint);
                case "long": return typeof(long);
                case "ulong": return typeof(ulong);
                case "float": return typeof(float);
                case "double": return typeof(double);
                case "string": return typeof(string);
                case "v2": return typeof(Vector2);
                case "v3": return typeof(Vector3);
                case "v4": return typeof(Vector4);
                case "q": return typeof(Quaternion);
                case "color": return typeof(Color);
                case "color32": return typeof(Color32);
                case "url": return typeof(VRCUrl);
                default:
                    MultiSimLog.Warn($"Unsupported network event parameter type tag '{tag}'.");
                    return null;
            }
        }

        private static DataToken ValueToToken(object obj)
        {
            switch (obj)
            {
                case bool b: return new DataToken(b);
                case char c: return new DataToken(c.ToString());
                case byte by: return new DataToken((double)by);
                case sbyte sb: return new DataToken((double)sb);
                case short s: return new DataToken((double)s);
                case ushort us: return new DataToken((double)us);
                case int i: return new DataToken((double)i);
                case uint u: return new DataToken((double)u);
                case long l: return new DataToken((double)l);
                case ulong ul: return new DataToken((double)ul);
                case float f: return new DataToken((double)f);
                case double d: return new DataToken(d);
                case string str: return new DataToken(str);
                case Vector2 v2: return v2.GetJTokenFromVector2();
                case Vector3 v3: return v3.GetJTokenFromVector3();
                case Vector4 v4: return v4.GetJTokenFromVector4();
                case Quaternion q: return q.GetJTokenFromQuaternion();
                case Color color: return color.GetJTokenFromColor();
                case Color32 color32: return color32.GetJTokenFromColor32();
                case VRCUrl url: return new DataToken(url.Get());
                default: return new DataToken();
            }
        }

        private static object TokenToValue(Type type, DataToken token)
        {
            if (type == typeof(bool)) return token.Boolean;
            if (type == typeof(char)) return token.String.Length > 0 ? token.String[0] : ' ';
            if (type == typeof(byte)) return (byte)token.Double;
            if (type == typeof(sbyte)) return (sbyte)token.Double;
            if (type == typeof(short)) return (short)token.Double;
            if (type == typeof(ushort)) return (ushort)token.Double;
            if (type == typeof(int)) return (int)token.Double;
            if (type == typeof(uint)) return (uint)token.Double;
            if (type == typeof(long)) return (long)token.Double;
            if (type == typeof(ulong)) return (ulong)token.Double;
            if (type == typeof(float)) return (float)token.Double;
            if (type == typeof(double)) return token.Double;
            if (type == typeof(string)) return token.TokenType == TokenType.String ? token.String : null;
            if (type == typeof(Vector2)) return token.GetVector2();
            if (type == typeof(Vector3)) return token.GetVector3();
            if (type == typeof(Vector4)) return token.GetVector4();
            if (type == typeof(Quaternion)) return token.GetQuaternion();
            if (type == typeof(Color)) return token.GetColor();
            if (type == typeof(Color32)) return token.GetColor32();
            if (type == typeof(VRCUrl)) return new VRCUrl(token.String);
            return null;
        }

        #endregion
    }
}
#endif
