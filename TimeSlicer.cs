//#define VERBOSE_LOGGING
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Cratesmith.Utils;
using UnityEngine.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Cratesmith.Timeslicer
{
    [System.Serializable]
    public class TimeSlicer
    {
        public float    currentUnscaledTime    { get; private set; }
        public float    currentTime            { get; private set; }
        public float    maxExecutionTime       { get; set; }
        public int      scheduledTickCount     { get; private set; }
        public int      overdueTickCount       { get; private set; }
        public float    lastUpdateDuration     { get; private set; }
        public float    averageUpdateDuration  { get; private set; }

        public TimeSlicer(float time, float unscaledTime, float maxExecutionTime = 0.002f)
        {
            currentTime = time;
            currentUnscaledTime = unscaledTime;
            this.maxExecutionTime = maxExecutionTime;
            averageUpdateDuration = -1;
        }

        private static void BeginSample(string name)
        {
            Profiler.BeginSample(name);
            #if VERBOSE_LOGGING
            Debug.Log($"TimeSlicer({Time.frameCount}) {name}");
            #endif
        }

        public void Update(float time, float unscaledTime)
        {
            BeginSample("Timeslicer.Update");
            var dt = time - currentTime;
            var uDt = unscaledTime - currentUnscaledTime;
            float taskEstimatedTime = 0;
            currentTime = time;
            currentUnscaledTime = unscaledTime;

            m_stopwatch.Reset();
            m_stopwatch.Start();

            // Tick ALL slices that have reached or exceeded maximum time
            BeginSample("Overhead - Overdue scheduled items");
            int newOverdueTickCount = 0;
            OnUpdate_HandleOverdueSlicesSorted(currentTime, ref taskEstimatedTime, ref newOverdueTickCount,
                m_activeTimeSlicesByMaxTime);
            OnUpdate_HandleOverdueSlicesSorted(currentUnscaledTime, ref taskEstimatedTime, ref newOverdueTickCount,
                m_activeTimeSlicesByMaxUnscaledTime);
            Profiler.EndSample();

            // Move any items from delayed to active
            BeginSample("Overhead - delayed items");
            int delayedMoveCount = 0;
            int i = 0;
            var current = m_delayedTimeSlicesUnsorted.First;
            while (i < m_delayedTimeSlicesUnsorted.Count)
            {
                var timeslice = current.Value;
                var remove = false;
                var prev = current;
                current = current.Next;
                if (timeslice.maxNextTickAtTime <= currentTime)
                {
                    BeginSample("Ticks-Overdue (from inactive list)");
                    taskEstimatedTime += timeslice.tickDurationEstimate;
                    AddToExecutionList(timeslice);
                    remove = true;
                    Profiler.EndSample();
                }
                else if (m_stopwatch.Elapsed.TotalSeconds + taskEstimatedTime < maxExecutionTime)
                {
                    if (timeslice.usesUnscaledTime && timeslice.minNextTickAtTime <= currentUnscaledTime)
                    {
                        AddSorted(m_tempNewActiveTimeSlicesByMaxUnscaledTime, timeslice, s_compareByMaxTime);
                        remove = true;
                    }
                    else if (!timeslice.usesUnscaledTime && timeslice.minNextTickAtTime <= currentTime)
                    {
                        AddSorted(m_tempNewActiveTimeSlicesByMaxTime, timeslice, s_compareByMaxTime);
                        remove = true;
                    }
                }

                if (remove)
                {
                    m_delayedTimeSlicesUnsorted.RemoveNode(prev);
                    ++delayedMoveCount;
                }
                else
                {
                    ++i;
                }
            }

            MergeSorted(m_tempNewActiveTimeSlicesByMaxUnscaledTime, m_activeTimeSlicesByMaxUnscaledTime,
                s_compareByMaxTime);
            m_tempNewActiveTimeSlicesByMaxUnscaledTime.Clear();

            MergeSorted(m_tempNewActiveTimeSlicesByMaxTime, m_activeTimeSlicesByMaxTime, s_compareByMaxTime);
            m_tempNewActiveTimeSlicesByMaxTime.Clear();

            Profiler.EndSample();

            // 2.2 tick as many other slices as we can
            BeginSample("Overhead - Scheduled items");
            int newScheduledTickCount = 0;
            OnUpdate_HandleScheduledTasks(ref taskEstimatedTime, ref newScheduledTickCount,
                m_activeTimeSlicesByMaxUnscaledTime);
            OnUpdate_HandleScheduledTasks(ref taskEstimatedTime, ref newScheduledTickCount, m_activeTimeSlicesByMaxTime);

            BeginSample("Ticks");
            TickAndClearExecutionList(dt, uDt);
            Profiler.EndSample();

            Profiler.EndSample();
            m_stopwatch.Stop();
            Profiler.EndSample();

            overdueTickCount = newOverdueTickCount;
            scheduledTickCount = newScheduledTickCount;

            lastUpdateDuration = (float)m_stopwatch.Elapsed.TotalMilliseconds;
            averageUpdateDuration = averageUpdateDuration >= 0 
                ? lastUpdateDuration
                : lastUpdateDuration * 0.1f + averageUpdateDuration * 0.9f;

#if VERBOSE_LOGGING
        if (overdueTickCout > 0 || scheduledTickCount > 0)
        {
            Debug.LogFormat("TimeSlicer.Update: frame={0}\t" +
                            "Current Time={1}\t" +
                            "Overdue Ticks={2},\t" +
                            "Scheduled ticks={3}", 
                            Time.frameCount,
                            currentUnscaledTime, 
                            overdueTickCout,
                            scheduledTickCount);            
        }
#endif
        }

        private void OnUpdate_HandleScheduledTasks(ref float taskEstimatedTime, ref int scheduledTickCount,
            PreallocLinkList<ITimeslice> activeTimeSliceList)
        {
            while (activeTimeSliceList.First != null)
            {
                var timeslice = activeTimeSliceList.First.Value;
                if (m_stopwatch.Elapsed.TotalSeconds + taskEstimatedTime + timeslice.tickDurationEstimate >
                    maxExecutionTime)
                {
                    break;
                }

                activeTimeSliceList.RemoveFirst();

                taskEstimatedTime += timeslice.tickDurationEstimate;
                AddToExecutionList(timeslice);

                ++scheduledTickCount;
            }
        }

        private void OnUpdate_HandleOverdueSlicesSorted(float time, ref float taskEstimatedTime,
            ref int overdueTickCout, PreallocLinkList<ITimeslice> activeSlicesList)
        {
            while (activeSlicesList.First != null)
            {
                var timeslice = activeSlicesList.First.Value;
                if (timeslice.maxNextTickAtTime > time)
                {
                    break;
                }

                activeSlicesList.RemoveFirst();
                taskEstimatedTime += timeslice.tickDurationEstimate;
                AddToExecutionList(timeslice);
                ++overdueTickCout;
            }
        }

        private void TickAndClearExecutionList(float dt, float uDt)
        {
            BeginSample("TickAndClearExecutionList");
            for (int i = 0; i < m_tempExecutionLists.Count; i++)
            {
                var list = m_tempExecutionLists[i].Value;
                PreallocLinkList<ITimeslice>.Node src = null;
                while (src != list.Last)
                {
                    src = src != null ? src.Next : list.First;
                    Update_DoTick(dt, uDt, src.Value);
                }

                list.Clear();
            }

            Profiler.EndSample(); 
        }

        private void AddToExecutionList(ITimeslice timeslice)
        {
            BeginSample("AddToExecutionList");
            PreallocLinkList<ITimeslice> list = null;
            int executionOrder = timeslice.executionOrder;

            var index = m_tempExecutionLists.BinarySearch(
                new KeyValuePair<int, PreallocLinkList<ITimeslice>>(executionOrder, null), s_compareByExecutionOrder);
            if (index < 0)
            {
                list = new PreallocLinkList<ITimeslice>();
                m_tempExecutionLists.Insert(~index,
                    new KeyValuePair<int, PreallocLinkList<ITimeslice>>(executionOrder, list));
            }
            else
            {
                list = m_tempExecutionLists[index].Value;
            }

            list.Add(timeslice);
            Profiler.EndSample();
        }

        private void Update_DoTick(float dt, float uDt, ITimeslice timeslice)
        {
            BeginSample("Tick");
            var startTime = m_stopwatch.Elapsed.TotalSeconds;
            timeslice.Tick(dt, uDt);
            timeslice.lastTickDuration = (float) (m_stopwatch.Elapsed.TotalSeconds - startTime);
            Profiler.EndSample();
        }

        public void Add(ITimeslice timeslice)
        {
            timeslice.addedAtUnscaledTime = timeslice.usesUnscaledTime ? currentUnscaledTime : currentTime;

            if (timeslice.usesUnscaledTime && timeslice.minNextTickAtTime < currentUnscaledTime)
            {
                AddSorted(m_tempNewActiveTimeSlicesByMaxUnscaledTime, timeslice, s_compareByMaxTime);
            }
            else if (!timeslice.usesUnscaledTime && timeslice.minNextTickAtTime < currentTime)
            {
                AddSorted(m_tempNewActiveTimeSlicesByMaxTime, timeslice, s_compareByMaxTime);
            }
            else
            {
                m_delayedTimeSlicesUnsorted.Add(timeslice);
            }
        }

        public void Remove(ITimeslice timeslice)
        {
            m_delayedTimeSlicesUnsorted.Remove(timeslice);
            m_activeTimeSlicesByMaxUnscaledTime.Remove(timeslice);
        }

        static void MergeSorted<T>(PreallocLinkList<T> srcList, PreallocLinkList<T> destList, IComparer<T> comparer)
        {
            if (srcList.Count == 0)
            {
                return;
            }

            if (destList.Count == 0)
            {
                foreach (var item in srcList)
                {
                    destList.AddLast(item);
                }

                return;
            }


            PreallocLinkList<T>.Node src = null;
            PreallocLinkList<T>.Node dest = null;
            while (src != srcList.Last)
            {
                src = src != null ? src.Next : srcList.First;
                while (dest != destList.Last)
                {
                    dest = dest != null ? dest.Next : destList.First;
                    if (comparer.Compare(src.Value, dest.Value) < 0)
                    {
                        break;
                    }
                }

                destList.AddAfter(dest, src.Value);
            }
        }

        static void AddSorted<T>(PreallocLinkList<T> @this, T item, IComparer<T> comparer)
        {
            BeginSample("AddSorted");
            if (@this.Count == 0)
            {
                @this.AddFirst(item);
                Profiler.EndSample();
                return;
            }

            if (comparer.Compare(@this.Last.Value, item) <= 0)
            {
                @this.AddLast(item);
                Profiler.EndSample();
                return;
            }

            if (comparer.Compare(@this.First.Value, item) >= 0)
            {
                @this.AddFirst(item);
                Profiler.EndSample();
                return;
            }

            var current = @this.First;
            while (current.Next != null && comparer.Compare(current.Value, item) <= 0)
            {
                current = current.Next;
            }

            @this.AddAfter(current, item);
            Profiler.EndSample();
        }

        #region internal 
        readonly PreallocLinkList<ITimeslice> m_delayedTimeSlicesUnsorted = new PreallocLinkList<ITimeslice>();
        readonly PreallocLinkList<ITimeslice> m_activeTimeSlicesByMaxUnscaledTime = new PreallocLinkList<ITimeslice>();
        readonly PreallocLinkList<ITimeslice> m_activeTimeSlicesByMaxTime = new PreallocLinkList<ITimeslice>();
        readonly Stopwatch m_stopwatch = new Stopwatch();

        readonly List<KeyValuePair<int, PreallocLinkList<ITimeslice>>> m_tempExecutionLists =
            new List<KeyValuePair<int, PreallocLinkList<ITimeslice>>>();

        readonly PreallocLinkList<ITimeslice> m_tempNewActiveTimeSlicesByMaxTime = new PreallocLinkList<ITimeslice>();

        readonly PreallocLinkList<ITimeslice> m_tempNewActiveTimeSlicesByMaxUnscaledTime =
            new PreallocLinkList<ITimeslice>();

        static readonly CompareByMaxTime s_compareByMaxTime = new CompareByMaxTime();

        class CompareByMaxTime : IComparer<ITimeslice>
        {
            int IComparer<ITimeslice>.Compare(ITimeslice x, ITimeslice y)
            {
                if (x == y) return 0;
                if (x == null) return -1;
                if (y == null) return 1;
                return x.maxNextTickAtTime.CompareTo(y.maxNextTickAtTime);
            }
        }

        static readonly CompareByExecutionOrder s_compareByExecutionOrder = new CompareByExecutionOrder();

        class CompareByExecutionOrder : IComparer<KeyValuePair<int, PreallocLinkList<ITimeslice>>>
        {
            int IComparer<KeyValuePair<int, PreallocLinkList<ITimeslice>>>.Compare(
                KeyValuePair<int, PreallocLinkList<ITimeslice>> x, KeyValuePair<int, PreallocLinkList<ITimeslice>> y)
            {
                return x.Key.CompareTo(y.Key);
            }
        }

        #endregion
    }
}