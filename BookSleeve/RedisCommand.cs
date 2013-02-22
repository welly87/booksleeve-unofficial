using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BookSleeve
{
    internal abstract class RedisMessage
    {
        private static readonly byte[][] literals;
        private static readonly RedisLiteral[] dbFree;

        private static readonly byte[]
            oneByteIntegerPrefix = Encoding.ASCII.GetBytes("$1\r\n"),
            twoByteIntegerPrefix = Encoding.ASCII.GetBytes("$2\r\n");

        private static readonly byte[] Crlf = Encoding.ASCII.GetBytes("\r\n");

        private readonly RedisLiteral command;
        private readonly int db;
        private bool critical;
        private RedisLiteral expected = RedisLiteral.None;
        private IMessageResult messageResult;
        private int messageState;

        static RedisMessage()
        {
            Array arr = Enum.GetValues(typeof (RedisLiteral));
            literals = new byte[arr.Length][];
            foreach (RedisLiteral literal in arr)
            {
                literals[(int) literal] = Encoding.ASCII.GetBytes(literal.ToString().ToUpperInvariant());
            }
            var tmp = new List<RedisLiteral>();
            FieldInfo[] fields = typeof (RedisLiteral).GetFields(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].IsDefined(typeof (DbFreeAttribute), false))
                    tmp.Add((RedisLiteral) fields[i].GetValue(null));
            }
            dbFree = tmp.ToArray();
        }

        protected RedisMessage(int db, RedisLiteral command)
        {
            bool isDbFree = false;
            for (int i = 0; i < dbFree.Length; i++)
            {
                if (dbFree[i] == command)
                {
                    isDbFree = true;
                    break;
                }
            }
            if (isDbFree)
            {
                if (db >= 0) throw new ArgumentOutOfRangeException("db", "A db is not required for " + command);
            }
            else
            {
                if (db < 0) throw new ArgumentOutOfRangeException("db", "A db must be specified for " + command);
            }
            this.db = db;
            this.command = command;
        }

        public bool MustSucceed
        {
            get { return critical; }
        }

        public byte[] Expected
        {
            get { return expected == RedisLiteral.None ? null : literals[(int) expected]; }
        }

        public int Db
        {
            get { return db; }
        }

        public RedisLiteral Command
        {
            get { return command; }
        }

        public RedisMessage Critical()
        {
            critical = true;
            return this;
        }

        public RedisMessage ExpectOk()
        {
            return Expect(RedisLiteral.OK);
        }

        public RedisMessage Expect(RedisLiteral result)
        {
            if (expected == RedisLiteral.None)
            {
                expected = result;
            }
            else
            {
                throw new InvalidOperationException();
            }
            return this;
        }

        internal void SetMessageResult(IMessageResult messageResult)
        {
            if (Interlocked.CompareExchange(ref this.messageResult, messageResult, null) != null)
            {
                throw new InvalidOperationException("A message-result is already assigned");
            }
        }

        internal virtual void Complete(RedisResult result)
        {
#if VERBOSE
            Trace.WriteLine("< " + command);
#endif
            IMessageResult snapshot = Interlocked.Exchange(ref messageResult, null); // only run once
            ChangeState(MessageState.Sent, MessageState.Complete);
            if (snapshot != null)
            {
                snapshot.Complete(result);
            }
        }

        internal bool ChangeState(MessageState from, MessageState to)
        {
            return Interlocked.CompareExchange(ref messageState, (int) to, (int) from) == (int) from;
        }

        public static RedisMessage Create(int db, RedisLiteral command)
        {
            return new RedisMessageNix(db, command);
        }

        public static RedisMessage Create(int db, RedisLiteral command, RedisParameter arg0)
        {
            return new RedisMessageUni(db, command, arg0);
        }

        public static RedisMessage Create(int db, RedisLiteral command, string arg0)
        {
            return new RedisMessageUniString(db, command, arg0);
        }

        public static RedisMessage Create(int db, RedisLiteral command, string arg0, string arg1)
        {
            return new RedisMessageBiString(db, command, arg0, arg1);
        }

        public static RedisMessage Create(int db, RedisLiteral command, string arg0, string[] args)
        {
            if (args == null) return Create(db, command, arg0);
            switch (args.Length)
            {
                case 0:
                    return Create(db, command, arg0);
                case 1:
                    return Create(db, command, arg0, args[0]);
                default:
                    return new RedisMessageMultiString(db, command, arg0, args);
            }
        }

        public static RedisMessage Create(int db, RedisLiteral command, RedisParameter arg0, RedisParameter arg1)
        {
            return new RedisMessageBi(db, command, arg0, arg1);
        }

        public static RedisMessage Create(int db, RedisLiteral command, RedisParameter arg0, RedisParameter arg1,
                                          RedisParameter arg2)
        {
            return new RedisMessageTri(db, command, arg0, arg1, arg2);
        }

        public static RedisMessage Create(int db, RedisLiteral command, RedisParameter arg0, RedisParameter arg1,
                                          RedisParameter arg2, RedisParameter arg3)
        {
            return new RedisMessageQuad(db, command, arg0, arg1, arg2, arg3);
        }

        public abstract void Write(Stream stream);

        public static RedisMessage Create(int db, RedisLiteral command, string[] args)
        {
            if (args == null) return new RedisMessageNix(db, command);
            switch (args.Length)
            {
                case 0:
                    return new RedisMessageNix(db, command);
                case 1:
                    return new RedisMessageUni(db, command, args[0]);
                case 2:
                    return new RedisMessageBi(db, command, args[0], args[1]);
                case 3:
                    return new RedisMessageTri(db, command, args[0], args[1], args[2]);
                case 4:
                    return new RedisMessageQuad(db, command, args[0], args[1], args[2], args[3]);
                default:
                    return new RedisMessageMulti(db, command, Array.ConvertAll(args, s => (RedisParameter) s));
            }
        }

        public static RedisMessage Create(int db, RedisLiteral command, params RedisParameter[] args)
        {
            if (args == null) return new RedisMessageNix(db, command);
            switch (args.Length)
            {
                case 0:
                    return new RedisMessageNix(db, command);
                case 1:
                    return new RedisMessageUni(db, command, args[0]);
                case 2:
                    return new RedisMessageBi(db, command, args[0], args[1]);
                case 3:
                    return new RedisMessageTri(db, command, args[0], args[1], args[2]);
                case 4:
                    return new RedisMessageQuad(db, command, args[0], args[1], args[2], args[3]);
                default:
                    return new RedisMessageMulti(db, command, args);
            }
        }

        public override string ToString()
        {
            return db >= 0 ? (db + ": " + command) : command.ToString();
        }

        protected void WriteCommand(Stream stream, int argCount)
        {
            try
            {
#if VERBOSE
                Trace.WriteLine("> " + command);
#endif
                stream.WriteByte((byte) '*');
                WriteRaw(stream, argCount + 1);
                WriteUnified(stream, command);
            }
            catch
            {
                throw;
            }
        }

        protected static void WriteUnified(Stream stream, RedisLiteral value)
        {
            WriteUnified(stream, literals[(int) value]);
        }

        protected static void WriteUnified(Stream stream, string value)
        {
            WriteUnified(stream, Encoding.UTF8.GetBytes(value));
        }

        protected static void WriteUnified(Stream stream, byte[] value)
        {
            stream.WriteByte((byte) '$');
            WriteRaw(stream, value.Length);
            stream.Write(value, 0, value.Length);
            stream.Write(Crlf, 0, 2);
        }

        protected static void WriteUnified(Stream stream, long value)
        {
            // note: need to use string version "${len}\r\n{data}\r\n", not intger version ":{data}\r\n"
            // when this is part of a multi-block message (which unified *is*)
            if (value >= 0 && value <= 99)
            {
                // low positive integers are very common; special-case them
                var i = (int) value;
                if (i <= 9)
                {
                    stream.Write(oneByteIntegerPrefix, 0, oneByteIntegerPrefix.Length);
                    stream.WriteByte((byte) ('0' + i));
                }
                else
                {
                    stream.Write(twoByteIntegerPrefix, 0, twoByteIntegerPrefix.Length);
                    stream.WriteByte((byte) ('0' + (i/10)));
                    stream.WriteByte((byte) ('0' + (i%10)));
                }
            }
            else
            {
                // not *quite* as efficient, but fine
                byte[] bytes = Encoding.ASCII.GetBytes(value.ToString());
                stream.WriteByte((byte) '$');
                WriteRaw(stream, bytes.Length);
                stream.Write(bytes, 0, bytes.Length);
            }
            stream.Write(Crlf, 0, 2);
        }

        protected static void WriteUnified(Stream stream, double value)
        {
            int i;
            if (value >= int.MinValue && value <= int.MaxValue && (i = (int) value) == value)
            {
                WriteUnified(stream, i); // use integer handling
            }
            else
            {
                WriteUnified(stream, ToString(value));
            }
        }

        private static string ToString(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        internal static string ToString(double value)
        {
            if (double.IsInfinity(value))
            {
                if (double.IsPositiveInfinity(value)) return "+inf";
                if (double.IsNegativeInfinity(value)) return "-inf";
            }
            return value.ToString("G", CultureInfo.InvariantCulture);
        }

        protected static void WriteRaw(Stream stream, long value)
        {
            if (value >= 0 && value <= 9)
            {
                stream.WriteByte((byte) ('0' + (int) value));
            }
            else if (value < 0 && value >= -9)
            {
                stream.WriteByte((byte) '-');
                stream.WriteByte((byte) ('0' - (int) value));
            }
            else
            {
                byte[] bytes = Encoding.ASCII.GetBytes(value.ToString());
                stream.Write(bytes, 0, bytes.Length);
            }
            stream.Write(Crlf, 0, 2);
        }

        private sealed class RedisMessageBi : RedisMessage
        {
            private readonly RedisParameter arg0, arg1;

            public RedisMessageBi(int db, RedisLiteral command, RedisParameter arg0, RedisParameter arg1)
                : base(db, command)
            {
                this.arg0 = arg0;
                this.arg1 = arg1;
            }

            public override void Write(Stream stream)
            {
                WriteCommand(stream, 2);
                arg0.Write(stream);
                arg1.Write(stream);
            }

            public override string ToString()
            {
                return base.ToString() + " " + arg0 + " " + arg1;
            }
        }

        private sealed class RedisMessageBiString : RedisMessage
        {
            private readonly string arg0, arg1;

            public RedisMessageBiString(int db, RedisLiteral command, string arg0, string arg1)
                : base(db, command)
            {
                if (arg0 == null) throw new ArgumentNullException("arg0");
                if (arg1 == null) throw new ArgumentNullException("arg1");
                this.arg0 = arg0;
                this.arg1 = arg1;
            }

            public override void Write(Stream stream)
            {
                WriteCommand(stream, 2);
                WriteUnified(stream, arg0);
                WriteUnified(stream, arg1);
            }

            public override string ToString()
            {
                return base.ToString() + " " + arg0 + " " + arg1;
            }
        }

        private sealed class RedisMessageMulti : RedisMessage
        {
            private readonly RedisParameter[] args;

            public RedisMessageMulti(int db, RedisLiteral command, RedisParameter[] args)
                : base(db, command)
            {
                this.args = args;
            }

            public override void Write(Stream stream)
            {
                if (args == null)
                {
                    WriteCommand(stream, 0);
                }
                else
                {
                    WriteCommand(stream, args.Length);
                    for (int i = 0; i < args.Length; i++)
                        args[i].Write(stream);
                }
            }

            public override string ToString()
            {
                var sb = new StringBuilder(base.ToString());
                for (int i = 0; i < args.Length; i++)
                    sb.Append(" ").Append(args[i]);
                return sb.ToString();
            }
        }

        private sealed class RedisMessageMultiString : RedisMessage
        {
            private readonly string arg0;
            private readonly string[] args;

            public RedisMessageMultiString(int db, RedisLiteral command, string arg0, string[] args)
                : base(db, command)
            {
                if (arg0 == null) throw new ArgumentNullException("arg0");
                if (args == null) throw new ArgumentNullException("args");
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == null) throw new ArgumentNullException("args:" + i);
                }
                this.arg0 = arg0;
                this.args = args;
            }

            public override void Write(Stream stream)
            {
                WriteCommand(stream, 1 + args.Length);
                WriteUnified(stream, arg0);
                for (int i = 0; i < args.Length; i++)
                    WriteUnified(stream, args[i]);
            }

            public override string ToString()
            {
                var sb = new StringBuilder(base.ToString());
                for (int i = 0; i < args.Length; i++)
                    sb.Append(" ").Append(args[i]);
                return sb.ToString();
            }
        }

        private sealed class RedisMessageNix : RedisMessage
        {
            public RedisMessageNix(int db, RedisLiteral command)
                : base(db, command)
            {
            }

            public override void Write(Stream stream)
            {
                WriteCommand(stream, 0);
            }
        }

        private sealed class RedisMessageQuad : RedisMessage
        {
            private readonly RedisParameter arg0, arg1, arg2, arg3;

            public RedisMessageQuad(int db, RedisLiteral command, RedisParameter arg0, RedisParameter arg1,
                                    RedisParameter arg2, RedisParameter arg3)
                : base(db, command)
            {
                this.arg0 = arg0;
                this.arg1 = arg1;
                this.arg2 = arg2;
                this.arg3 = arg3;
            }

            public override void Write(Stream stream)
            {
                WriteCommand(stream, 4);
                arg0.Write(stream);
                arg1.Write(stream);
                arg2.Write(stream);
                arg3.Write(stream);
            }

            public override string ToString()
            {
                return base.ToString() + " " + arg0 + " " + arg1 + " " + arg2 + " " + arg3;
            }
        }

        private sealed class RedisMessageTri : RedisMessage
        {
            private readonly RedisParameter arg0, arg1, arg2;

            public RedisMessageTri(int db, RedisLiteral command, RedisParameter arg0, RedisParameter arg1,
                                   RedisParameter arg2)
                : base(db, command)
            {
                this.arg0 = arg0;
                this.arg1 = arg1;
                this.arg2 = arg2;
            }

            public override void Write(Stream stream)
            {
                WriteCommand(stream, 3);
                arg0.Write(stream);
                arg1.Write(stream);
                arg2.Write(stream);
            }

            public override string ToString()
            {
                return base.ToString() + " " + arg0 + " " + arg1 + " " + arg2;
            }
        }

        private sealed class RedisMessageUni : RedisMessage
        {
            private readonly RedisParameter arg0;

            public RedisMessageUni(int db, RedisLiteral command, RedisParameter arg0)
                : base(db, command)
            {
                this.arg0 = arg0;
            }

            public override void Write(Stream stream)
            {
                WriteCommand(stream, 1);
                arg0.Write(stream);
            }

            public override string ToString()
            {
                return base.ToString() + " " + arg0;
            }
        }

        private sealed class RedisMessageUniString : RedisMessage
        {
            private readonly string arg0;

            public RedisMessageUniString(int db, RedisLiteral command, string arg0)
                : base(db, command)
            {
                if (arg0 == null) throw new ArgumentNullException("arg0");
                this.arg0 = arg0;
            }

            public override void Write(Stream stream)
            {
                WriteCommand(stream, 1);
                WriteUnified(stream, arg0);
            }

            public override string ToString()
            {
                return base.ToString() + " " + arg0;
            }
        }

        internal abstract class RedisParameter
        {
            public static implicit operator RedisParameter(RedisLiteral value)
            {
                return new RedisLiteralParameter(value);
            }

            public static implicit operator RedisParameter(string value)
            {
                return new RedisStringParameter(value);
            }

            public static implicit operator RedisParameter(byte[] value)
            {
                return new RedisBlobParameter(value);
            }

            public static implicit operator RedisParameter(long value)
            {
                return new RedisInt64Parameter(value);
            }

            public static implicit operator RedisParameter(double value)
            {
                return new RedisDoubleParameter(value);
            }

            public static RedisParameter Range(long value, bool inclusive)
            {
                if (inclusive) return new RedisInt64Parameter(value);
                return new RedisStringParameter("(" + RedisMessage.ToString(value));
            }

            public static RedisParameter Range(double value, bool inclusive)
            {
                if (inclusive) return new RedisDoubleParameter(value);
                return new RedisStringParameter("(" + RedisMessage.ToString(value));
            }

            public abstract void Write(Stream stream);

            internal static RedisParameter Create(object value)
            {
                if (value == null) throw new ArgumentNullException("value");
                switch (Type.GetTypeCode(value.GetType()))
                {
                    case TypeCode.String:
                        return (string) value;
                    case TypeCode.Single:
                        return (float) value;
                    case TypeCode.Double:
                        return (double) value;
                    case TypeCode.Byte:
                        return (byte) value;
                    case TypeCode.SByte:
                        return (sbyte) value;
                    case TypeCode.Int16:
                        return (short) value;
                    case TypeCode.Int32:
                        return (int) value;
                    case TypeCode.Int64:
                        return (long) value;
                    case TypeCode.Boolean:
                        return (bool) value ? 1 : 0;
                    default:
                        var blob = value as byte[];
                        if (blob != null) return blob;
                        throw new ArgumentException("Data-type not supported: " + value.GetType());
                }
            }

            private class RedisBlobParameter : RedisParameter
            {
                private readonly byte[] value;

                public RedisBlobParameter(byte[] value)
                {
                    if (value == null) throw new ArgumentNullException("value");
                    this.value = value;
                }

                public override void Write(Stream stream)
                {
                    WriteUnified(stream, value);
                }

                public override string ToString()
                {
                    if (value == null) return "**NULL**";
                    return "{" + value.Length.ToString() + " bytes}";
                }
            }

            private class RedisDoubleParameter : RedisParameter
            {
                private readonly double value;

                public RedisDoubleParameter(double value)
                {
                    this.value = value;
                }

                public override void Write(Stream stream)
                {
                    WriteUnified(stream, value);
                }

                public override string ToString()
                {
                    return value.ToString();
                }
            }

            private class RedisInt64Parameter : RedisParameter
            {
                private readonly long value;

                public RedisInt64Parameter(long value)
                {
                    this.value = value;
                }

                public override void Write(Stream stream)
                {
                    WriteUnified(stream, value);
                }

                public override string ToString()
                {
                    return value.ToString();
                }
            }

            private class RedisLiteralParameter : RedisParameter
            {
                private readonly RedisLiteral value;

                public RedisLiteralParameter(RedisLiteral value)
                {
                    this.value = value;
                }

                public override void Write(Stream stream)
                {
                    WriteUnified(stream, value);
                }

                public override string ToString()
                {
                    return value.ToString();
                }
            }

            private class RedisStringParameter : RedisParameter
            {
                private readonly string value;

                public RedisStringParameter(string value)
                {
                    if (value == null) throw new ArgumentNullException("value");
                    this.value = value;
                }

                public override void Write(Stream stream)
                {
                    WriteUnified(stream, value);
                }

                public override string ToString()
                {
                    if (value == null) return "**NULL**";
                    if (value.Length < 20) return "\"" + value + "\"";
                    return "\"" + value.Substring(0, 15) + "...[" + value.Length.ToString() + "]";
                }
            }
        }
    }

    internal class QueuedMessage : RedisMessage
    {
        private readonly RedisMessage innnerMessage;

        public QueuedMessage(RedisMessage innnerMessage)
            : base(innnerMessage.Db, innnerMessage.Command)
        {
            if (innnerMessage == null) throw new ArgumentNullException("innnerMessage");
            this.innnerMessage = innnerMessage;
            Expect(RedisLiteral.QUEUED).Critical();
        }

        public RedisMessage InnerMessage
        {
            get { return innnerMessage; }
        }

        public override void Write(Stream stream)
        {
            innnerMessage.Write(stream);
        }
    }

    internal interface IMultiMessage
    {
        void Execute(RedisConnectionBase redisConnectionBase, ref int currentDb);
    }

    internal class MultiMessage : RedisMessage, IMultiMessage
    {
        private readonly List<Condition> conditions;
        private readonly ExecMessage exec;

        private readonly RedisMessage[] messages;

        public MultiMessage(RedisConnection parent, RedisMessage[] messages, List<Condition> conditions, object state)
            : base(-1, RedisLiteral.MULTI)
        {
            exec = new ExecMessage(parent, state);
            this.conditions = conditions;
            this.messages = messages;
            ExpectOk().Critical();
        }

        public Task<bool> Completion
        {
            get { return exec.Completion; }
        }

        void IMultiMessage.Execute(RedisConnectionBase conn, ref int currentDb)
        {
            RedisMessage[] pending = messages;
            int estimateCount = pending.Length;

            if (ExecutePreconditions(conn, ref currentDb))
            {
                conn.WriteRaw(this); // MULTI
                var newlyQueued = new List<QueuedMessage>(pending.Length);
                for (int i = 0; i < pending.Length; i++)
                {
                    conn.WriteMessage(ref currentDb, pending[i], newlyQueued);
                }
                newlyQueued.TrimExcess();
                conn.WriteMessage(ref currentDb, Execute(newlyQueued), null);
            }
            else
            {
                // preconditions failed; ABORT
                conn.WriteMessage(ref currentDb, Create(-1, RedisLiteral.UNWATCH).ExpectOk().Critical(), null);
                exec.Complete(RedisResult.Multi(null)); // spoof a rollback; same appearance to the caller
            }
        }

        private bool ExecutePreconditions(RedisConnectionBase conn, ref int currentDb)
        {
            if (conditions == null || conditions.Count == 0) return true;

            Task lastTask = null;
            foreach (Condition cond in conditions)
            {
                lastTask = cond.Task;
                foreach (RedisMessage msg in cond.CreateMessages())
                {
                    conn.WriteMessage(ref currentDb, msg, null);
                }
            }
            conn.Flush(true); // make sure we send it all

            // now need to check all the preconditions passed
            if (lastTask != null) conn.Wait(lastTask);

            foreach (Condition cond in conditions)
            {
                if (!cond.Validate()) return false;
            }

            return true;
        }

        public override void Write(Stream stream)
        {
            WriteCommand(stream, 0);
        }

        public RedisMessage Execute(List<QueuedMessage> queued)
        {
            exec.SetQueued(queued);
            return exec;
        }
    }

    internal class ExecMessage : RedisMessage, IMessageResult
    {
        private readonly TaskCompletionSource<bool> completion;
        private readonly RedisConnection parent;
        private QueuedMessage[] queued;

        public ExecMessage(RedisConnection parent, object state)
            : base(-1, RedisLiteral.EXEC)
        {
            if (parent == null) throw new ArgumentNullException("parent");
            completion = new TaskCompletionSource<bool>(state);
            SetMessageResult(this);
            this.parent = parent;
            Critical();
        }

        public Task<bool> Completion
        {
            get { return completion.Task; }
        }

        Task IMessageResult.Task
        {
            get { return completion.Task; }
        }

        void IMessageResult.Complete(RedisResult result)
        {
            if (result.IsCancellation)
            {
                completion.SetCanceled();
                SetInnerReplies(result);
            }
            else if (result.IsError)
            {
                completion.SetException(result.Error());
                SetInnerReplies(result);
            }
            else
            {
                try
                {
                    if (result.IsNil)
                    {
                        // aborted
                        SetInnerReplies(RedisResult.Cancelled);
                        completion.SetResult(false);
                    }
                    else
                    {
                        RedisResult[] items = result.ValueItems;
                        if (items.Length != (queued == null ? 0 : queued.Length))
                            throw new InvalidOperationException(string.Format("{0} results expected, {1} received",
                                                                              queued.Length, items.Length));

                        for (int i = 0; i < items.Length; i++)
                        {
                            RedisResult reply = items[i];
                            object ctx = parent.ProcessReply(ref reply, queued[i].InnerMessage);
                            parent.ProcessCallbacks(ctx, reply);
                        }
                        completion.SetResult(true);
                    }
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                    throw;
                }
            }
        }

        public override void Write(Stream stream)
        {
            WriteCommand(stream, 0);
        }

        internal void SetQueued(List<QueuedMessage> queued)
        {
            if (queued == null) throw new ArgumentNullException("queued");
            if (this.queued != null) throw new InvalidOperationException();
            this.queued = queued.ToArray();
        }

        private void SetInnerReplies(RedisResult result)
        {
            if (queued != null)
            {
                for (int i = 0; i < queued.Length; i++)
                {
                    RedisResult reply = result; // need to be willing for this to be mutated
                    object ctx = parent.ProcessReply(ref reply, queued[i].InnerMessage);
                    parent.ProcessCallbacks(ctx, reply);
                }
            }
        }
    }

    internal class PingMessage : RedisMessage
    {
        private readonly DateTime created;
        private DateTime received;
        private DateTime sent;

        public PingMessage()
            : base(-1, RedisLiteral.PING)
        {
            created = DateTime.UtcNow;
            Expect(RedisLiteral.PONG).Critical();
        }

        public override void Write(Stream stream)
        {
            WriteCommand(stream, 0);
            if (sent == DateTime.MinValue) sent = DateTime.UtcNow;
        }

        internal override void Complete(RedisResult result)
        {
            received = DateTime.UtcNow;
            base.Complete(result.IsError
                              ? result
                              : new RedisResult.TimingRedisResult(
                                    sent - created, received - sent));
        }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    internal sealed class DbFreeAttribute : Attribute
    {
    }

    internal enum RedisLiteral
    {
        None = 0,
        // responses
        OK,
        QUEUED,
        PONG,
        // commands (extracted from http://redis.io/commands)
        APPEND,
        [DbFree] AUTH,
        BGREWRITEAOF,
        BITCOUNT,
        BITOP,
        BGSAVE,
        BLPOP,
        BRPOP,
        BRPOPLPUSH,
        [DbFree] CLIENT,
        [DbFree] SETNAME,
        [DbFree] CONFIG,
        GET,
        SET,
        RESETSTAT,
        DBSIZE,
        DEBUG,
        OBJECT,
        SEGFAULT,
        DECR,
        DECRBY,
        DEL,
        [DbFree] DISCARD,
        [DbFree] ECHO,
        EVAL,
        EVALSHA,
        [DbFree] EXEC,
        EXISTS,
        EXPIRE,
        EXPIREAT,
        [DbFree] FLUSHALL,
        FLUSHDB,
        GETBIT,
        GETRANGE,
        GETSET,
        HDEL,
        HEXISTS,
        HGET,
        HGETALL,
        HINCRBY,
        HINCRBYFLOAT,
        HKEYS,
        HLEN,
        HMGET,
        HMSET,
        HSET,
        HSETNX,
        HVALS,
        INCR,
        INCRBY,
        INCRBYFLOAT,
        [DbFree] INFO,
        KEYS,
        LASTSAVE,
        LINDEX,
        LINSERT,
        LLEN,
        LPOP,
        LPUSH,
        LPUSHX,
        LRANGE,
        LREM,
        LSET,
        LTRIM,
        MGET,
        [DbFree] MONITOR,
        MOVE,
        MSET,
        MSETNX,
        [DbFree] MULTI,
        PERSIST,
        [DbFree] PING,
        [DbFree] PSUBSCRIBE,
        [DbFree] PUBLISH,
        [DbFree] PUNSUBSCRIBE,
        [DbFree] QUIT,
        RANDOMKEY,
        RENAME,
        RENAMENX,
        RPOP,
        RPOPLPUSH,
        RPUSH,
        RPUSHX,
        SADD,
        SAVE,
        SCARD,
        [DbFree] SCRIPT,
        [DbFree] SENTINEL,
        SDIFF,
        SDIFFSTORE,
        SELECT,
        SETBIT,
        SETEX,
        SETNX,
        SETRANGE,
        SHUTDOWN,
        SINTER,
        SINTERSTORE,
        SISMEMBER,
        [DbFree] SLAVEOF,
        SLOWLOG,
        SMEMBERS,
        SMOVE,
        SORT,
        SPOP,
        SRANDMEMBER,
        SREM,
        STRLEN,
        [DbFree] SUBSCRIBE,
        SUBSTR,
        SUNION,
        SUNIONSTORE,
        SYNC,
        TTL,
        TYPE,
        [DbFree] UNSUBSCRIBE,
        [DbFree] UNWATCH,
        WATCH,
        ZADD,
        ZCARD,
        ZCOUNT,
        ZINCRBY,
        ZINTERSTORE,
        ZRANGE,
        ZRANGEBYSCORE,
        ZRANK,
        ZREM,
        ZREMRANGEBYRANK,
        ZREMRANGEBYSCORE,
        ZREVRANGE,
        ZREVRANGEBYSCORE,
        ZREVRANK,
        ZSCORE,
        ZUNIONSTORE,
        // other
        NO,
        ONE,
        WITHSCORES,
        LIMIT,
        LOAD,
        BEFORE,
        AFTER,
        AGGREGATE,
        WEIGHTS,
        SUM,
        MIN,
        MAX,
        FLUSH,
        AND,
        OR,
        NOT,
        XOR,
        LIST,
        KILL,
        STORE,
        BY,
        ALPHA,
        DESC
    }
}