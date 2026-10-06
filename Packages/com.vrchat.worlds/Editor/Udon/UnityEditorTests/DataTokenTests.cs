using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace Tests.DataContainers
{
    public class DataTokenTests
    {

        [Test]
        public void TestIsNull()
        {
            Assert.IsTrue(new DataToken().IsNull);
            Assert.IsFalse(new DataToken((byte)5).IsNull);
            Assert.IsFalse(new DataToken((sbyte)5).IsNull);
            Assert.IsFalse(new DataToken((short)5).IsNull);
            Assert.IsFalse(new DataToken((ushort)5).IsNull);
            Assert.IsFalse(new DataToken((int)5).IsNull);
            Assert.IsFalse(new DataToken((uint)5).IsNull);
            Assert.IsFalse(new DataToken((long)5).IsNull);
            Assert.IsFalse(new DataToken((ulong)5).IsNull);
            Assert.IsFalse(new DataToken((float)5).IsNull);
            Assert.IsFalse(new DataToken((double)5).IsNull);
            Assert.IsFalse(new DataToken(true).IsNull);
            Assert.IsFalse(new DataToken("string").IsNull);
            string nullstring = null;
            Assert.IsTrue(new DataToken(nullstring).IsNull);
            Assert.IsFalse(new DataToken(new DataList(){"4", "5"}).IsNull);
            Assert.IsFalse(new DataToken(new DataDictionary(){["key"]="value"}).IsNull);
            Assert.IsFalse(new DataToken(new bool[] {false, true, false}).IsNull);
        }
        
        [Test]
        public void TestEmpty()
        {
            SetAndGet("empty", new DataToken());
        }

        [Test]
        public void TestBool()
        {
            SetAndGet("bool true", new DataToken(true));
            SetAndGet("bool false", new DataToken(false));
        }
        [Test]
        public void TestByte()
        {
            SetAndGet("byte number", (byte)4);
            SetAndGet("max byte number", byte.MaxValue);
            SetAndGet("min byte number", byte.MinValue);
        }
        [Test]
        public void TestSByte()
        {
            SetAndGet("sbyte number", (sbyte)4);
            SetAndGet("max sbyte number", sbyte.MaxValue);
            SetAndGet("min sbyte number", sbyte.MinValue);
        }
        [Test]
        public void TestShort()
        {
            SetAndGet("short number", (short)4);
            SetAndGet("max short number", short.MaxValue);
            SetAndGet("min short number", short.MinValue);
        }
        [Test]
        public void TestUShort()
        {
            SetAndGet("ushort number", (ushort)4);
            SetAndGet("max ushort number", ushort.MaxValue);
            SetAndGet("min ushort number", ushort.MinValue);
        }
        [Test]
        public void TestInt()
        {
            SetAndGet("int number", (int)4);
            SetAndGet("max int number", int.MaxValue);
            SetAndGet("min int number", int.MinValue);
        }
        [Test]
        public void TestUInt()
        {
            SetAndGet("uint number", (uint)4);
            SetAndGet("max uint number", uint.MaxValue);
            SetAndGet("min uint number", uint.MinValue);
        }
        [Test]
        public void TestLong()
        {
            SetAndGet("long number", (long)4);
            SetAndGet("max long number", long.MaxValue);
            SetAndGet("min long number", long.MinValue);
        }
        [Test]
        public void TestULong()
        {
            SetAndGet("ulong number", (ulong)4);
            SetAndGet("max ulong number", ulong.MaxValue);
            SetAndGet("min ulong number", ulong.MinValue);
        }
        [Test]
        public void TestFloat()
        {
            SetAndGet("float number", 0.123f);
            SetAndGet("max float number", float.MaxValue);
            SetAndGet("large float number", float.MinValue);
            SetAndGet("epsilon float number", float.Epsilon);
            SetAndGet("Negative Infinity float number", float.NegativeInfinity);
            SetAndGet("Positive Infinity float number", float.PositiveInfinity);
            SetAndGet("NaN float number", float.NaN);
            SetAndGet("Epsilon float number", float.Epsilon);
        }
        [Test]
        public void TestDouble()
        {
            SetAndGet("double number", 0.12341233);
            SetAndGet("max double number", double.MaxValue);
            SetAndGet("min double number", double.MinValue);
            SetAndGet("Negative Infinity double number", double.NegativeInfinity);
            SetAndGet("Positive Infinity double number", double.PositiveInfinity);
            SetAndGet("NaN double number", double.NaN);
            SetAndGet("Epsilon double number", double.Epsilon);
        }

        [Test]
        public void TestString()
        {
            SetAndGet("backspace", new DataToken("\b"));
            SetAndGet("form feed", new DataToken("\f"));
            SetAndGet("newline", new DataToken("\n"));
            SetAndGet("carriage return", new DataToken("\r"));
            SetAndGet("tab", new DataToken("\t"));
            SetAndGet("quotes", new DataToken("\""));
            SetAndGet("victory hand", new DataToken("✌"));
            SetAndGet("mandarin", new DataToken("䉟"));
            SetAndGet("greater than", new DataToken(">"));
        }

        [Test]
        public void TestReference()
        {
            SetAndGet("bool array reference", new DataToken(new bool[] {false, true, false}));
        }

        [Test]
        public void TestBitcast()
        {
            // test value requires 5 bytes, larger than an int at 4
            var token = new DataToken(0x12_3456_7890UL);
            Assert.AreEqual(0x12_3456_7890UL, token.ULong);
            Assert.AreEqual(TokenType.ULong, token.TokenType);

            // test that normal cast fails
            Assert.Throws<InvalidOperationException>(() => _ = token.Long);

            // test that bitcast works
            var bitlong = token.Bitcast(TokenType.Long);
            Assert.AreEqual(0x12_3456_7890L, bitlong.Long);
            Assert.AreEqual(TokenType.Long, bitlong.TokenType);

            // truncate the original long
            var bitint = token.Bitcast(TokenType.Int);
            Assert.AreEqual(0x3456_7890, bitint.Int);
            Assert.AreEqual(TokenType.Int, bitint.TokenType);
            // back to ulong and it should have been zero-extended
            var bitint_aslong = bitint.Bitcast(TokenType.ULong);
            Assert.AreEqual(0x3456_7890UL, bitint_aslong.ULong);
            Assert.AreEqual(TokenType.ULong, bitint_aslong.TokenType);
            // as byte it will be truncated further
            var bitint_asbyte = bitint.Bitcast(TokenType.Byte);
            Assert.AreEqual(0x90, bitint_asbyte.Byte);
            Assert.AreEqual(TokenType.Byte, bitint_asbyte.TokenType);
            // converting back from byte will once again zero-extend, the 0x12345678 has been lost, only 0x90 remains
            var bitint_back = bitint_asbyte.Bitcast(TokenType.Int);
            Assert.AreEqual(0x90, bitint_back.Int);
            Assert.AreEqual(TokenType.Int, bitint_back.TokenType);

            // test that bitcasting to types where the bitpattern might be invalid still preserves it
            var bitdouble = token.Bitcast(TokenType.Double);
            Assert.AreNotEqual(1.0f, bitdouble.Double);
            Assert.AreEqual(TokenType.Double, bitdouble.TokenType);
            var bitdouble_back = bitdouble.Bitcast(TokenType.ULong);
            Assert.AreEqual(0x12_3456_7890UL, bitdouble_back.ULong);
            Assert.AreEqual(TokenType.ULong, bitdouble_back.TokenType);

            // test that bitcast fails on incompatible types
            Assert.Throws<InvalidOperationException>(() => _ = token.Bitcast(TokenType.String));
            var stringtoken = new DataToken("hello");
            Assert.Throws<InvalidOperationException>(() => _ = stringtoken.Bitcast(TokenType.Float));
        }

        [Test]
        public void TestEquality()
        {
            void TestEqualityOfDifferentTokensWithSameValue<T>(T _a, T _b)
            {
                DataToken a = new DataToken(_a);
                DataToken b = new DataToken(_b);
                if (_a == null && _b == null)
                {
                    // DataTokens both referring to null are expected to be equal
                    Assert.IsTrue(a == b);
                }
                else if (typeof(T) == typeof(string))
                {
                    // Strings are special and their == operator does NOT check if the references are exactly the same, rather it checks the characters
                    Assert.IsTrue(a == b);
                }
                else
                {
                    Assert.IsFalse(a == b);
                }
                Assert.IsTrue(a.Equals(b));
            }

            TestEqualityOfDifferentTokensWithSameValue(true, true);
            TestEqualityOfDifferentTokensWithSameValue((sbyte)5, (sbyte)5);
            TestEqualityOfDifferentTokensWithSameValue((byte)5, (byte)5);
            TestEqualityOfDifferentTokensWithSameValue((short)5, (short)5);
            TestEqualityOfDifferentTokensWithSameValue((ushort)5, (ushort)5);
            TestEqualityOfDifferentTokensWithSameValue((int)5, (int)5);
            TestEqualityOfDifferentTokensWithSameValue((uint)5, (uint)5);
            TestEqualityOfDifferentTokensWithSameValue((long)5, (long)5);
            TestEqualityOfDifferentTokensWithSameValue((ulong)5, (ulong)5);
            TestEqualityOfDifferentTokensWithSameValue((float)5, (float)5);
            TestEqualityOfDifferentTokensWithSameValue((double)5, (double)5);
            TestEqualityOfDifferentTokensWithSameValue("abc", "abc");
            TestEqualityOfDifferentTokensWithSameValue<object>(null, null);
            TestEqualityOfDifferentTokensWithSameValue(new Vector3Int(1, 2, 3), new Vector3Int(1, 2, 3));
            
            void TestEqualityOfDifferentTokensWithDifferentValues<T>(T _a, T _b)
            {
                DataToken a = new DataToken(_a);
                DataToken b = new DataToken(_b);
                Assert.IsFalse(a == b);
                Assert.IsFalse(a.Equals(b));
            }
            
            TestEqualityOfDifferentTokensWithDifferentValues(true, false);
            TestEqualityOfDifferentTokensWithDifferentValues((sbyte)5, (sbyte)6);
            TestEqualityOfDifferentTokensWithDifferentValues((byte)5, (byte)6);
            TestEqualityOfDifferentTokensWithDifferentValues((short)5, (short)6);
            TestEqualityOfDifferentTokensWithDifferentValues((ushort)5, (ushort)6);
            TestEqualityOfDifferentTokensWithDifferentValues((int)5, (int)6);
            TestEqualityOfDifferentTokensWithDifferentValues((uint)5, (uint)6);
            TestEqualityOfDifferentTokensWithDifferentValues((long)5, (long)6);
            TestEqualityOfDifferentTokensWithDifferentValues((ulong)5, (ulong)6);
            TestEqualityOfDifferentTokensWithDifferentValues((float)5, (float)6);
            TestEqualityOfDifferentTokensWithDifferentValues((double)5, (double)6);
            TestEqualityOfDifferentTokensWithDifferentValues("abc", "def");
            TestEqualityOfDifferentTokensWithDifferentValues("abc", null);
            TestEqualityOfDifferentTokensWithDifferentValues(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6));
            
            // Make sure types do not implicitly cast
            Assert.IsFalse(new DataToken((sbyte)5).Equals(new DataToken((byte)5)));
            Assert.IsFalse(new DataToken((short)5).Equals(new DataToken((ushort)5)));
            Assert.IsFalse(new DataToken((int)5).Equals(new DataToken((uint)5)));
            Assert.IsFalse(new DataToken((long)5).Equals(new DataToken((ulong)5)));
            Assert.IsFalse(new DataToken((float)5).Equals(new DataToken((double)5)));

            Assert.IsFalse(new DataToken((sbyte)5) == new DataToken((byte)5));
            Assert.IsFalse(new DataToken((short)5) == new DataToken((ushort)5));
            Assert.IsFalse(new DataToken((int)5) == new DataToken((uint)5));
            Assert.IsFalse(new DataToken((long)5) == new DataToken((ulong)5));
            Assert.IsFalse(new DataToken((float)5) == new DataToken((double)5));
        }

        private void SetAndGet(string title, DataToken inToken)
        {
            SetAndGetDictionary(title, inToken);
            SetAndGetList(title, inToken);
        }
        private void SetAndGetDictionary(string title, DataToken inToken)
        {
            DataDictionary dataDictionary = new DataDictionary();
            dataDictionary.SetValue("key", inToken);
            Assert.IsTrue(dataDictionary.TryGetValue("key", out DataToken outToken), $"{title} Failed to get value with error {outToken}");
            Assert.AreEqual(inToken, outToken, $"{title} Input and output tokens were not the same");
        }
        private void SetAndGetList(string title, DataToken inToken)
        {
            DataList dataList = new DataList();
            dataList.Add(inToken);
            Assert.IsTrue(dataList.TryGetValue(0, out DataToken outToken), $"{title} Failed to get value with error {outToken}");
            Assert.AreEqual(inToken, outToken,  $"{title} Input and output tokens were not the same");
        }

        #region Type Conversion

        [Test]
        public void TestConvertBoolean()
        {
            const bool testBool = true;

            DataToken boolToken = new DataToken(testBool);

            // by definition, booleans can only be treated as booleans, everything else throws
            Assert.AreEqual(testBool, boolToken.Boolean);
            Assert.Throws<InvalidOperationException>(() => _ = boolToken.SByte);
            Assert.Throws<InvalidOperationException>(() => _ = boolToken.Byte);
            Assert.Throws<InvalidOperationException>(() => _ = boolToken.Short);
            Assert.Throws<InvalidOperationException>(() => _ = boolToken.UShort);
            Assert.Throws<InvalidOperationException>(() => _ = boolToken.Int);
            Assert.Throws<InvalidOperationException>(() => _ = boolToken.UInt);
            Assert.Throws<InvalidOperationException>(() => _ = boolToken.Long);
            Assert.Throws<InvalidOperationException>(() => _ = boolToken.ULong);
            Assert.Throws<InvalidOperationException>(() => _ = boolToken.Float);
            Assert.Throws<InvalidOperationException>(() => _ = boolToken.Double);
            Assert.Throws<InvalidOperationException>(() => _ = boolToken.Number);

            // not primitive
            Assert.Throws<InvalidOperationException>(() => _ = boolToken.String);
            Assert.Throws<InvalidOperationException>(() => _ = boolToken.DataList);
            Assert.Throws<InvalidOperationException>(() => _ = boolToken.DataDictionary);
            Assert.Throws<InvalidOperationException>(() => _ = boolToken.Reference);

            // not error
            Assert.AreEqual(DataError.None, boolToken.Error);
        }

        [Test]
        public void TestConvertSByte()
        {
            const sbyte testSByte = -1;

            DataToken sbyteToken = new DataToken(testSByte);

            // narrowing
            Assert.Throws<InvalidOperationException>(() => _ = sbyteToken.Boolean);
            Assert.Throws<InvalidOperationException>(() => _ = sbyteToken.Byte);
            Assert.Throws<InvalidOperationException>(() => _ = sbyteToken.UShort);
            Assert.Throws<InvalidOperationException>(() => _ = sbyteToken.UInt);
            Assert.Throws<InvalidOperationException>(() => _ = sbyteToken.ULong);

            // stable or widening
            Assert.AreEqual(testSByte, sbyteToken.SByte);
            Assert.AreEqual(testSByte, sbyteToken.Short);
            Assert.AreEqual(testSByte, sbyteToken.Int);
            Assert.AreEqual(testSByte, sbyteToken.Long);
            Assert.AreEqual(testSByte, sbyteToken.Float);
            Assert.AreEqual(testSByte, sbyteToken.Double);
            Assert.AreEqual(testSByte, sbyteToken.Number);

            // not primitive
            Assert.Throws<InvalidOperationException>(() => _ = sbyteToken.String);
            Assert.Throws<InvalidOperationException>(() => _ = sbyteToken.DataList);
            Assert.Throws<InvalidOperationException>(() => _ = sbyteToken.DataDictionary);
            Assert.Throws<InvalidOperationException>(() => _ = sbyteToken.Reference);

            // not error
            Assert.AreEqual(DataError.None, sbyteToken.Error);
        }

        [Test]
        public void TestConvertByte()
        {
            const byte testByte = 0;

            DataToken byteToken = new DataToken(testByte);

            // narrowing
            Assert.Throws<InvalidOperationException>(() => _ = byteToken.Boolean);
            Assert.Throws<InvalidOperationException>(() => _ = byteToken.SByte);

            // stable or widening
            Assert.AreEqual(testByte, byteToken.Byte);
            Assert.AreEqual(testByte, byteToken.Short);
            Assert.AreEqual(testByte, byteToken.UShort);
            Assert.AreEqual(testByte, byteToken.Int);
            Assert.AreEqual(testByte, byteToken.UInt);
            Assert.AreEqual(testByte, byteToken.Long);
            Assert.AreEqual(testByte, byteToken.ULong);
            Assert.AreEqual(testByte, byteToken.Float);
            Assert.AreEqual(testByte, byteToken.Double);
            Assert.AreEqual(testByte, byteToken.Number);

            // not primitive
            Assert.Throws<InvalidOperationException>(() => _ = byteToken.String);
            Assert.Throws<InvalidOperationException>(() => _ = byteToken.DataList);
            Assert.Throws<InvalidOperationException>(() => _ = byteToken.DataDictionary);
            Assert.Throws<InvalidOperationException>(() => _ = byteToken.Reference);

            // not error
            Assert.AreEqual(DataError.None, byteToken.Error);
        }

        [Test]
        public void TestConvertShort()
        {
            const short testShort = -1;

            DataToken shortToken = new DataToken(testShort);

            // narrowing
            Assert.Throws<InvalidOperationException>(() => _ = shortToken.Boolean);
            Assert.Throws<InvalidOperationException>(() => _ = shortToken.SByte);
            Assert.Throws<InvalidOperationException>(() => _ = shortToken.Byte);
            Assert.Throws<InvalidOperationException>(() => _ = shortToken.UShort);
            Assert.Throws<InvalidOperationException>(() => _ = shortToken.UInt);
            Assert.Throws<InvalidOperationException>(() => _ = shortToken.ULong);

            // stable or widening
            Assert.AreEqual(testShort, shortToken.Short);
            Assert.AreEqual(testShort, shortToken.Int);
            Assert.AreEqual(testShort, shortToken.Long);
            Assert.AreEqual(testShort, shortToken.Float);
            Assert.AreEqual(testShort, shortToken.Double);
            Assert.AreEqual(testShort, shortToken.Number);

            // not primitive
            Assert.Throws<InvalidOperationException>(() => _ = shortToken.String);
            Assert.Throws<InvalidOperationException>(() => _ = shortToken.DataList);
            Assert.Throws<InvalidOperationException>(() => _ = shortToken.DataDictionary);
            Assert.Throws<InvalidOperationException>(() => _ = shortToken.Reference);

            // not error
            Assert.AreEqual(DataError.None, shortToken.Error);
        }

        [Test]
        public void TestConvertUShort()
        {
            const ushort testUShort = 0;

            DataToken ushortToken = new DataToken(testUShort);

            // narrowing
            Assert.Throws<InvalidOperationException>(() => _ = ushortToken.Boolean);
            Assert.Throws<InvalidOperationException>(() => _ = ushortToken.SByte);
            Assert.Throws<InvalidOperationException>(() => _ = ushortToken.Byte);
            Assert.Throws<InvalidOperationException>(() => _ = ushortToken.Short);

            // stable or widening
            Assert.AreEqual(testUShort, ushortToken.UShort);
            Assert.AreEqual(testUShort, ushortToken.Int);
            Assert.AreEqual(testUShort, ushortToken.UInt);
            Assert.AreEqual(testUShort, ushortToken.Long);
            Assert.AreEqual(testUShort, ushortToken.ULong);
            Assert.AreEqual(testUShort, ushortToken.Float);
            Assert.AreEqual(testUShort, ushortToken.Double);
            Assert.AreEqual(testUShort, ushortToken.Number);

            // not primitive
            Assert.Throws<InvalidOperationException>(() => _ = ushortToken.String);
            Assert.Throws<InvalidOperationException>(() => _ = ushortToken.DataList);
            Assert.Throws<InvalidOperationException>(() => _ = ushortToken.DataDictionary);
            Assert.Throws<InvalidOperationException>(() => _ = ushortToken.Reference);

            // not error
            Assert.AreEqual(DataError.None, ushortToken.Error);
        }

        [Test]
        public void TestConvertInt()
        {
            const int testInt = -1;

            DataToken intToken = new DataToken(testInt);

            // narrowing
            Assert.Throws<InvalidOperationException>(() => _ = intToken.Boolean);
            Assert.Throws<InvalidOperationException>(() => _ = intToken.SByte);
            Assert.Throws<InvalidOperationException>(() => _ = intToken.Byte);
            Assert.Throws<InvalidOperationException>(() => _ = intToken.Short);
            Assert.Throws<InvalidOperationException>(() => _ = intToken.UShort);
            Assert.Throws<InvalidOperationException>(() => _ = intToken.UInt);
            Assert.Throws<InvalidOperationException>(() => _ = intToken.ULong);

            // stable or widening
            Assert.AreEqual(testInt, intToken.Int);
            Assert.AreEqual(testInt, intToken.Long);
            Assert.AreEqual(testInt, intToken.Float);
            Assert.AreEqual(testInt, intToken.Double);
            Assert.AreEqual(testInt, intToken.Number);

            // not primitive
            Assert.Throws<InvalidOperationException>(() => _ = intToken.String);
            Assert.Throws<InvalidOperationException>(() => _ = intToken.DataList);
            Assert.Throws<InvalidOperationException>(() => _ = intToken.DataDictionary);
            Assert.Throws<InvalidOperationException>(() => _ = intToken.Reference);

            // not error
            Assert.AreEqual(DataError.None, intToken.Error);
        }

        [Test]
        public void TestConvertUInt()
        {
            const uint testUInt = 0;

            DataToken uintToken = new DataToken(testUInt);

            // narrowing
            Assert.Throws<InvalidOperationException>(() => _ = uintToken.Boolean);
            Assert.Throws<InvalidOperationException>(() => _ = uintToken.SByte);
            Assert.Throws<InvalidOperationException>(() => _ = uintToken.Byte);
            Assert.Throws<InvalidOperationException>(() => _ = uintToken.Short);
            Assert.Throws<InvalidOperationException>(() => _ = uintToken.UShort);
            Assert.Throws<InvalidOperationException>(() => _ = uintToken.Int);

            // stable or widening
            Assert.AreEqual(testUInt, uintToken.UInt);
            Assert.AreEqual(testUInt, uintToken.Long);
            Assert.AreEqual(testUInt, uintToken.ULong);
            Assert.AreEqual(testUInt, uintToken.Float);
            Assert.AreEqual(testUInt, uintToken.Double);
            Assert.AreEqual(testUInt, uintToken.Number);

            // not primitive
            Assert.Throws<InvalidOperationException>(() => _ = uintToken.String);
            Assert.Throws<InvalidOperationException>(() => _ = uintToken.DataList);
            Assert.Throws<InvalidOperationException>(() => _ = uintToken.DataDictionary);
            Assert.Throws<InvalidOperationException>(() => _ = uintToken.Reference);

            // not error
            Assert.AreEqual(DataError.None, uintToken.Error);
        }

        [Test]
        public void TestConvertLong()
        {
            const long testLong = -1;

            DataToken longToken = new DataToken(testLong);

            // narrowing
            Assert.Throws<InvalidOperationException>(() => _ = longToken.Boolean);
            Assert.Throws<InvalidOperationException>(() => _ = longToken.SByte);
            Assert.Throws<InvalidOperationException>(() => _ = longToken.Byte);
            Assert.Throws<InvalidOperationException>(() => _ = longToken.Short);
            Assert.Throws<InvalidOperationException>(() => _ = longToken.UShort);
            Assert.Throws<InvalidOperationException>(() => _ = longToken.Int);
            Assert.Throws<InvalidOperationException>(() => _ = longToken.UInt);
            Assert.Throws<InvalidOperationException>(() => _ = longToken.ULong);

            // stable or widening
            Assert.AreEqual(testLong, longToken.Long);
            Assert.AreEqual(testLong, longToken.Float);
            Assert.AreEqual(testLong, longToken.Double);
            Assert.AreEqual(testLong, longToken.Number);

            // not primitive
            Assert.Throws<InvalidOperationException>(() => _ = longToken.String);
            Assert.Throws<InvalidOperationException>(() => _ = longToken.DataList);
            Assert.Throws<InvalidOperationException>(() => _ = longToken.DataDictionary);
            Assert.Throws<InvalidOperationException>(() => _ = longToken.Reference);

            // not error
            Assert.AreEqual(DataError.None, longToken.Error);
        }

        [Test]
        public void TestConvertULong()
        {
            const ulong testULong = 0;

            DataToken ulongToken = new DataToken(testULong);

            // narrowing
            Assert.Throws<InvalidOperationException>(() => _ = ulongToken.Boolean);
            Assert.Throws<InvalidOperationException>(() => _ = ulongToken.SByte);
            Assert.Throws<InvalidOperationException>(() => _ = ulongToken.Byte);
            Assert.Throws<InvalidOperationException>(() => _ = ulongToken.Short);
            Assert.Throws<InvalidOperationException>(() => _ = ulongToken.UShort);
            Assert.Throws<InvalidOperationException>(() => _ = ulongToken.Int);
            Assert.Throws<InvalidOperationException>(() => _ = ulongToken.UInt);
            Assert.Throws<InvalidOperationException>(() => _ = ulongToken.Long);

            // stable or widening
            Assert.AreEqual(testULong, ulongToken.ULong);
            Assert.AreEqual(testULong, ulongToken.Float);
            Assert.AreEqual(testULong, ulongToken.Double);
            Assert.AreEqual(testULong, ulongToken.Number);

            // not primitive
            Assert.Throws<InvalidOperationException>(() => _ = ulongToken.String);
            Assert.Throws<InvalidOperationException>(() => _ = ulongToken.DataList);
            Assert.Throws<InvalidOperationException>(() => _ = ulongToken.DataDictionary);
            Assert.Throws<InvalidOperationException>(() => _ = ulongToken.Reference);

            // not error
            Assert.AreEqual(DataError.None, ulongToken.Error);
        }

        [Test]
        public void TestConvertSingleFloat()
        {
            const float testFloat = 1.5f;

            DataToken floatToken = new DataToken(testFloat);

            // narrowing
            Assert.Throws<InvalidOperationException>(() => _ = floatToken.Boolean);
            Assert.Throws<InvalidOperationException>(() => _ = floatToken.SByte);
            Assert.Throws<InvalidOperationException>(() => _ = floatToken.Byte);
            Assert.Throws<InvalidOperationException>(() => _ = floatToken.Short);
            Assert.Throws<InvalidOperationException>(() => _ = floatToken.UShort);
            Assert.Throws<InvalidOperationException>(() => _ = floatToken.Int);
            Assert.Throws<InvalidOperationException>(() => _ = floatToken.UInt);
            Assert.Throws<InvalidOperationException>(() => _ = floatToken.Long);
            Assert.Throws<InvalidOperationException>(() => _ = floatToken.ULong);

            // stable or widening
            Assert.AreEqual(testFloat, floatToken.Float);
            Assert.AreEqual(testFloat, floatToken.Double);
            Assert.AreEqual(testFloat, floatToken.Number);

            // not primitive
            Assert.Throws<InvalidOperationException>(() => _ = floatToken.String);
            Assert.Throws<InvalidOperationException>(() => _ = floatToken.DataList);
            Assert.Throws<InvalidOperationException>(() => _ = floatToken.DataDictionary);
            Assert.Throws<InvalidOperationException>(() => _ = floatToken.Reference);

            // not error
            Assert.AreEqual(DataError.None, floatToken.Error);
        }

        [Test]
        public void TestConvertDoubleFloat()
        {
            const double testDouble = 1.5;

            DataToken doubleToken = new DataToken(testDouble);

            // narrowing
            Assert.Throws<InvalidOperationException>(() => _ = doubleToken.Boolean);
            Assert.Throws<InvalidOperationException>(() => _ = doubleToken.SByte);
            Assert.Throws<InvalidOperationException>(() => _ = doubleToken.Byte);
            Assert.Throws<InvalidOperationException>(() => _ = doubleToken.Short);
            Assert.Throws<InvalidOperationException>(() => _ = doubleToken.UShort);
            Assert.Throws<InvalidOperationException>(() => _ = doubleToken.Int);
            Assert.Throws<InvalidOperationException>(() => _ = doubleToken.UInt);
            Assert.Throws<InvalidOperationException>(() => _ = doubleToken.Long);
            Assert.Throws<InvalidOperationException>(() => _ = doubleToken.ULong);
            Assert.Throws<InvalidOperationException>(() => _ = doubleToken.Float);

            // stable or widening
            Assert.AreEqual(testDouble, doubleToken.Double);
            Assert.AreEqual(testDouble, doubleToken.Number);

            // not primitive
            Assert.Throws<InvalidOperationException>(() => _ = doubleToken.String);
            Assert.Throws<InvalidOperationException>(() => _ = doubleToken.DataList);
            Assert.Throws<InvalidOperationException>(() => _ = doubleToken.DataDictionary);
            Assert.Throws<InvalidOperationException>(() => _ = doubleToken.Reference);

            // not error
            Assert.AreEqual(DataError.None, doubleToken.Error);
        }
        
        #endregion
    }
}
