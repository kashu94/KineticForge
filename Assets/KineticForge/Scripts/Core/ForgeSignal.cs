using System;
using UnityEngine;

namespace KineticForge.Core
{
    /// <summary>
    /// Shared storage and dispatch rules for <see cref="ForgeSignal"/> and <see cref="ForgeSignal{T}"/>.
    /// </summary>
    /// <typeparam name="TDelegate">Handler delegate type.</typeparam>
    /// <remarks>
    /// <para>Dispatch guarantees, which a plain multicast delegate does not give:</para>
    /// <list type="bullet">
    /// <item><description>
    /// Exception isolation: a handler that throws is logged with its stack trace, and the remaining handlers still run.
    /// One faulty UI or audio listener can never stall the game flow.
    /// </description></item>
    /// <item><description>
    /// Zero allocation: raising never allocates. Subscribing allocates only when the handler array has to grow.
    /// </description></item>
    /// <item><description>
    /// Safe mutation during dispatch: a handler removed while a raise is running is skipped from that point on.
    /// A handler added during a raise first runs on the next raise.
    /// </description></item>
    /// </list>
    /// <para>Handlers run in subscription order. Use from the Unity main thread only.</para>
    /// </remarks>
    public abstract class ForgeSignalBase<TDelegate> where TDelegate : Delegate
    {
        const int InitialCapacity = 4;

        readonly string _name;

        /// <summary>Handler slots. Slots removed during a raise hold null until compaction.</summary>
        protected TDelegate[] Handlers = new TDelegate[InitialCapacity];

        /// <summary>Number of used slots in <see cref="Handlers"/>, including null slots awaiting compaction.</summary>
        protected int SlotCount;

        int _raiseDepth;
        int _liveCount;
        bool _needsCompaction;

        /// <summary>Creates a signal.</summary>
        /// <param name="name">Name shown in exception reports, for example <c>ForgeEvents.StanceChanged</c>.</param>
        protected ForgeSignalBase(string name)
        {
            _name = string.IsNullOrEmpty(name) ? GetType().Name : name;
        }

        /// <summary>Name shown in exception reports.</summary>
        public string Name => _name;

        /// <summary>Number of subscribed handlers.</summary>
        public int SubscriberCount => _liveCount;

        /// <summary>Subscribes a handler. Null is ignored. The same handler may be added more than once.</summary>
        /// <param name="handler">Handler to add.</param>
        public void Add(TDelegate handler)
        {
            if (handler is null)
            {
                return;
            }

            if (SlotCount == Handlers.Length)
            {
                Array.Resize(ref Handlers, Handlers.Length * 2);
            }

            Handlers[SlotCount++] = handler;
            _liveCount++;
        }

        /// <summary>Unsubscribes the most recently added handler equal to <paramref name="handler"/>.</summary>
        /// <param name="handler">Handler to remove. Null or unknown handlers are ignored.</param>
        public void Remove(TDelegate handler)
        {
            if (handler is null)
            {
                return;
            }

            for (int i = SlotCount - 1; i >= 0; i--)
            {
                TDelegate candidate = Handlers[i];
                if (candidate is null || !candidate.Equals(handler))
                {
                    continue;
                }

                _liveCount--;
                if (_raiseDepth > 0)
                {
                    // Keep indices stable for the raise in progress; compact once it ends.
                    Handlers[i] = null;
                    _needsCompaction = true;
                }
                else
                {
                    SlotCount--;
                    Array.Copy(Handlers, i + 1, Handlers, i, SlotCount - i);
                    Handlers[SlotCount] = null;
                }

                return;
            }
        }

        /// <summary>Removes every handler. Safe to call from inside a handler.</summary>
        public void Clear()
        {
            _liveCount = 0;
            if (_raiseDepth > 0)
            {
                for (int i = 0; i < SlotCount; i++)
                {
                    Handlers[i] = null;
                }

                _needsCompaction = true;
                return;
            }

            Array.Clear(Handlers, 0, SlotCount);
            SlotCount = 0;
            _needsCompaction = false;
        }

        /// <summary>Marks the start of a raise. Pair every call with <see cref="EndRaise"/> in a finally block.</summary>
        /// <returns>The slot count to iterate; handlers added during the raise lie beyond it.</returns>
        protected int BeginRaise()
        {
            _raiseDepth++;
            return SlotCount;
        }

        /// <summary>Marks the end of a raise and compacts removed slots once the outermost raise finishes.</summary>
        protected void EndRaise()
        {
            _raiseDepth--;
            if (_raiseDepth == 0 && _needsCompaction)
            {
                Compact();
            }
        }

        /// <summary>Logs an exception thrown by a handler. Allocates, but only on this failure path.</summary>
        /// <param name="exception">The exception thrown.</param>
        /// <param name="handler">The handler that threw.</param>
        protected void ReportHandlerException(Exception exception, Delegate handler)
        {
            string method = handler.Method.DeclaringType != null
                ? handler.Method.DeclaringType.Name + "." + handler.Method.Name
                : handler.Method.Name;
            UnityEngine.Object context = handler.Target as UnityEngine.Object;

            ForgeLog.Error(ForgeLogChannel.Core, _name, "HANDLER_EXCEPTION",
                "Handler " + method + " threw " + exception.GetType().Name + ". The remaining handlers still ran.", context);
            Debug.LogException(exception, context);
        }

        void Compact()
        {
            int write = 0;
            for (int read = 0; read < SlotCount; read++)
            {
                TDelegate handler = Handlers[read];
                if (handler is null)
                {
                    continue;
                }

                Handlers[write++] = handler;
            }

            Array.Clear(Handlers, write, SlotCount - write);
            SlotCount = write;
            _needsCompaction = false;
        }
    }

    /// <summary>
    /// Exception-isolated, allocation-free signal without a payload. See <see cref="ForgeSignalBase{TDelegate}"/>.
    /// </summary>
    public sealed class ForgeSignal : ForgeSignalBase<Action>
    {
        /// <summary>Creates a signal.</summary>
        /// <param name="name">Name shown in exception reports.</param>
        public ForgeSignal(string name) : base(name)
        {
        }

        /// <summary>Invokes every handler in subscription order.</summary>
        public void Raise()
        {
            int count = BeginRaise();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Action handler = Handlers[i];
                    if (handler is null)
                    {
                        continue;
                    }

                    try
                    {
                        handler();
                    }
                    catch (Exception exception)
                    {
                        ReportHandlerException(exception, handler);
                    }
                }
            }
            finally
            {
                EndRaise();
            }
        }
    }

    /// <summary>
    /// Exception-isolated, allocation-free signal carrying a payload. See <see cref="ForgeSignalBase{TDelegate}"/>.
    /// </summary>
    /// <typeparam name="T">Payload type. Prefer small readonly structs.</typeparam>
    public sealed class ForgeSignal<T> : ForgeSignalBase<Action<T>>
    {
        /// <summary>Creates a signal.</summary>
        /// <param name="name">Name shown in exception reports.</param>
        public ForgeSignal(string name) : base(name)
        {
        }

        /// <summary>Invokes every handler in subscription order with <paramref name="payload"/>.</summary>
        /// <param name="payload">Payload delivered to each handler.</param>
        public void Raise(in T payload)
        {
            int count = BeginRaise();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Action<T> handler = Handlers[i];
                    if (handler is null)
                    {
                        continue;
                    }

                    try
                    {
                        handler(payload);
                    }
                    catch (Exception exception)
                    {
                        ReportHandlerException(exception, handler);
                    }
                }
            }
            finally
            {
                EndRaise();
            }
        }
    }
}
