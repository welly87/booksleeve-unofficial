using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;

namespace BookSleeve
{
    internal abstract class RedisResult
    {
        public static readonly RedisResult Pass = new PassRedisResult(),
                                           TimeoutNotSent = new TimeoutRedisResult("Timeout; the messsage was not sent"),
                                           TimeoutSent =
                                               new TimeoutRedisResult(
                                                   "Timeout; the messsage was sent and may still have effect"),
                                           Cancelled = new CancellationRedisResult();

        internal abstract bool IsNil { get; }

        public virtual long ValueInt64
        {
            get { return long.Parse(ValueString); }
        }

        public bool ValueBoolean
        {
            get { return ValueInt64 != 0; }
        }

        public virtual string ValueString
        {
            get
            {
                byte[] bytes;
                return (bytes = ValueBytes) == null
                           ? null
                           : bytes.Length == 0
                                 ? ""
                                 : Encoding.UTF8.GetString(bytes);
            }
        }

        public virtual byte[] ValueBytes
        {
            get { throw Error(); }
        }

        public virtual RedisResult[] ValueItems
        {
            get { return null; }
        }

        public virtual bool IsError
        {
            get { return false; }
        }

        public virtual bool IsCancellation
        {
            get { return false; }
        }

        public virtual double ValueDouble
        {
            get { return ParseDouble(ValueString); }
        }

        public abstract object Parse(bool inferStrings);

        internal static RedisResult Message(byte[] value)
        {
            return new MessageRedisResult(value);
        }

        internal static RedisResult Error(string value)
        {
            return new ErrorRedisResult(value);
        }

        internal static RedisResult Integer(long value)
        {
            return new Int64RedisResult(value);
        }

        internal static RedisResult Bytes(byte[] value)
        {
            return new BytesRedisResult(value);
        }

        internal static RedisResult Assert(string error)
        {
            return error == null ? Pass : new ErrorRedisResult(error);
        }

        internal void Assert()
        {
            if (IsError) throw Error();
        }

        public bool IsMatch(byte[] expected)
        {
            byte[] bytes = ValueBytes;
            if (expected == null && bytes == null) return true;
            if (expected == null || bytes == null || expected.Length != bytes.Length) return false;
            for (int i = 0; i < bytes.Length; i++)
                if (expected[i] != bytes[i]) return false;
            return true;
        }

        public virtual Exception Error()
        {
            return new InvalidOperationException("This operation is not supported by " + GetType().Name);
        }

        internal static double ParseDouble(string value)
        {
            if (string.Equals(value, "-inf", StringComparison.OrdinalIgnoreCase)) return double.NegativeInfinity;
            if (string.Equals(value, "inf", StringComparison.OrdinalIgnoreCase)) return double.PositiveInfinity;
            return double.Parse(value, CultureInfo.InvariantCulture);
        }

        internal static RedisResult Multi(RedisResult[] inner)
        {
            return new MultiRedisResult(inner);
        }

        public string[] ValueItemsString()
        {
            RedisResult[] items = ValueItems;
            if (items == null) return null;
            var result = new string[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                result[i] = items[i].ValueString;
            }
            return result;
        }

        public byte[][] ValueItemsBytes()
        {
            RedisResult[] items = ValueItems;
            if (items == null) return null;
            var result = new byte[items.Length][];
            for (int i = 0; i < items.Length; i++)
            {
                result[i] = items[i].ValueBytes;
            }
            return result;
        }

        public KeyValuePair<byte[], double>[] ExtractPairs()
        {
            RedisResult[] items = ValueItems;
            var pairs = new KeyValuePair<byte[], double>[items.Length/2];
            int index = 0;
            for (int i = 0; i < pairs.Length; i++)
            {
                byte[] itemKey = items[index++].ValueBytes;
                double itemScore = items[index++].ValueDouble;
                pairs[i] = new KeyValuePair<byte[], double>(itemKey, itemScore);
            }
            return pairs;
        }

        public KeyValuePair<string, double>[] ExtractStringDoublePairs()
        {
            RedisResult[] items = ValueItems;
            var pairs = new KeyValuePair<string, double>[items.Length/2];
            int index = 0;
            for (int i = 0; i < pairs.Length; i++)
            {
                string itemKey = items[index++].ValueString;
                double itemScore = items[index++].ValueDouble;
                pairs[i] = new KeyValuePair<string, double>(itemKey, itemScore);
            }
            return pairs;
        }

        public Dictionary<string, byte[]> ExtractHashPairs()
        {
            RedisResult[] items = ValueItems;
            int count = items.Length/2;
            var dict = new Dictionary<string, byte[]>(count);
            int index = 0;
            for (int i = 0; i < count; i++)
            {
                string itemKey = items[index++].ValueString;
                byte[] itemValue = items[index++].ValueBytes;
                dict.Add(itemKey, itemValue);
            }
            return dict;
        }

        public Dictionary<string, string> ExtractStringPairs()
        {
            RedisResult[] items = ValueItems;
            int count = items.Length/2;
            var dict = new Dictionary<string, string>(count);
            int index = 0;
            for (int i = 0; i < count; i++)
            {
                string itemKey = items[index++].ValueString;
                string itemValue = items[index++].ValueString;
                dict.Add(itemKey, itemValue);
            }
            return dict;
        }

        private class BytesRedisResult : RedisResult
        {
            private readonly byte[] value;

            internal BytesRedisResult(byte[] value)
            {
                this.value = value;
            }

            public override byte[] ValueBytes
            {
                get { return value; }
            }

            internal override bool IsNil
            {
                get { return value == null; }
            }

            public override object Parse(bool inferStrings)
            {
                if (inferStrings)
                {
                    try
                    {
                        string speculative = Encoding.UTF8.GetString(value);
                        byte[] tmp = Encoding.UTF8.GetBytes(speculative);
                        if (tmp.Length != value.Length) return value;
                        for (int i = 0; i < tmp.Length; i++)
                        {
                            if (tmp[i] != value[i]) return value;
                        }
                        return speculative;
                    }
                    catch
                    {
                        /* try only! */
                    }
                }
                return value;
            }
        }

        private class CancellationRedisResult : RedisResult
        {
            public override bool IsCancellation
            {
                get { return true; }
            }

            public override bool IsError
            {
                get { return true; }
            }

            internal override bool IsNil
            {
                get { return false; }
            }

            public override object Parse(bool inferStrings)
            {
                return Error();
            }

            public override Exception Error()
            {
                return new InvalidOperationException("The message was cancelled");
            }
        }

        private class ErrorRedisResult : RedisResult
        {
            private readonly string message;

            internal ErrorRedisResult(string message)
            {
                this.message = message ?? "";
            }

            public override bool IsError
            {
                get { return true; }
            }

            public override RedisResult[] ValueItems
            {
                get { throw Error(); }
            }

            internal override bool IsNil
            {
                get { return false; }
            }

            public override object Parse(bool inferStrings)
            {
                return Error();
            }

            public override Exception Error()
            {
                if (message.StartsWith("READONLY"))
                {
                    return new RedisReadonlySlaveException(message);
                }
                return new RedisException(message);
            }
        }

        private class Int64RedisResult : RedisResult
        {
            private readonly long value;

            internal Int64RedisResult(long value)
            {
                this.value = value;
            }

            public override long ValueInt64
            {
                get { return value; }
            }

            public override string ValueString
            {
                get { return value.ToString(); }
            }

            public override double ValueDouble
            {
                get { return value; }
            }

            internal override bool IsNil
            {
                get { return false; }
            }

            public override object Parse(bool inferStrings)
            {
                return value;
            }
        }

        private class MessageRedisResult : RedisResult
        {
            private readonly byte[] value;

            internal MessageRedisResult(byte[] value)
            {
                this.value = value;
            }

            public override byte[] ValueBytes
            {
                get { return value; }
            }

            internal override bool IsNil
            {
                get { return value == null; }
            }

            public override object Parse(bool inferStrings)
            {
                return ValueString;
            }
        }

        private class PassRedisResult : RedisResult
        {
            internal override bool IsNil
            {
                get { return false; }
            }

            public override object Parse(bool inferStrings)
            {
                return true;
            }
        }

        private class TimeoutRedisResult : RedisResult
        {
            private readonly string message;

            public TimeoutRedisResult(string message)
            {
                this.message = message;
            }

            public override bool IsError
            {
                get { return true; }
            }

            public override RedisResult[] ValueItems
            {
                get { throw Error(); }
            }

            internal override bool IsNil
            {
                get { return false; }
            }

            public override object Parse(bool inferStrings)
            {
                return Error();
            }

            public override Exception Error()
            {
                return new TimeoutException(message);
            }
        }

        internal class TimingRedisResult : RedisResult
        {
            private readonly TimeSpan receive;
            private readonly TimeSpan send;

            internal TimingRedisResult(TimeSpan send, TimeSpan receive)
            {
                this.send = send;
                this.receive = receive;
            }

            internal TimeSpan Send
            {
                get { return send; }
            }

            internal TimeSpan Receive
            {
                get { return receive; }
            }

            public override long ValueInt64
            {
                get { return (long) (send.TotalMilliseconds + receive.TotalMilliseconds); }
            }

            public override string ValueString
            {
                get { return ValueInt64.ToString(); }
            }

            public override byte[] ValueBytes
            {
                get { return Encoding.UTF8.GetBytes(ValueString); }
            }

            internal override bool IsNil
            {
                get { return false; }
            }

            public override object Parse(bool inferStrings)
            {
                return ValueInt64;
            }
        }
    }

    internal class MultiRedisResult : RedisResult
    {
        private readonly RedisResult[] items;

        public MultiRedisResult(RedisResult[] items)
        {
            this.items = items;
        }

        public override RedisResult[] ValueItems
        {
            get { return items; }
        }

        internal override bool IsNil
        {
            get { return items == null; }
        }

        public override object Parse(bool inferStrings)
        {
            var results = new object[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                results[i] = items[i].Parse(inferStrings);
            }
            return results;
        }
    }

    /// <summary>
    ///     A redis-related exception; this could represent a message from the server,
    ///     or a protocol error talking to the server.
    /// </summary>
    [Serializable]
    public class RedisException : Exception
    {
        /// <summary>
        ///     Create a new RedisException
        /// </summary>
        public RedisException()
        {
        }

        /// <summary>
        ///     Create a new RedisException
        /// </summary>
        public RedisException(string message) : base(message)
        {
        }

        /// <summary>
        ///     Create a new RedisException
        /// </summary>
        public RedisException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        ///     Create a new RedisException
        /// </summary>
        protected RedisException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    ///     A redis-related exception, where an attempt has been made to change a value on a readonly slave (2.6 or above)
    /// </summary>
    [Serializable]
    public sealed class RedisReadonlySlaveException : RedisException
    {
        /// <summary>
        ///     Create a new RedisReadonlySlaveException
        /// </summary>
        public RedisReadonlySlaveException()
        {
        }

        /// <summary>
        ///     Create a new RedisReadonlySlaveException
        /// </summary>
        public RedisReadonlySlaveException(string message) : base(message)
        {
        }

        /// <summary>
        ///     Create a new RedisReadonlySlaveException
        /// </summary>
        public RedisReadonlySlaveException(string message, Exception innerException) : base(message, innerException)
        {
        }

        private RedisReadonlySlaveException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }


    internal enum MessageState
    {
        NotSent,
        Sent,
        Complete,
        Cancelled
    }
}