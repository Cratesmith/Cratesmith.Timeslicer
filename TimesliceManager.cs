using Cratesmith.Actors;
using Cratesmith.ScriptExecutionOrder;
using UnityEditor;
using UnityEngine;
#if UNITY_EDITOR

#endif

namespace Cratesmith.Timeslicer
{
    /// <summary>
    /// Timeslice manager
    ///
    /// Coroutine usage:
    /// IEnumerator MyCoroutine()
    /// {
    ///     // create a slice. This records how long code took and allows the timeslicer to schedule it.
    ///     // 0.2f is the initial estimate. This gets updated by the average
    ///     var timeslice = TimeSliceManager.Get().CreateCoroutineTimeslice(this, 0.2f); 
    ///     using(timeslice.Begin()) // start timing!
    ///     {
    ///         Thread.Sleep(TimeSpan.FromSeconds(0.2f); // example of some expensive code
    ///     } // ends timing!
    ///     yield return timeslice.Wait(0.5f, 1.0f, true); // dont call for at least 0.5s, at most 1.0s, use unscaled time = true
    /// }
    /// 
    /// </summary>
    [ScriptExecutionOrder(-999)]
    public class TimesliceManager : DontDestroySingleton<TimesliceManager>
    {
        public float m_BudgetMS = 2.0f;
    
        public TimeSlicer TimeSlicer { get; private set; }

        protected override void OnAwake()
        {
            TimeSlicer = new TimeSlicer(Time.time, Time.unscaledTime, m_BudgetMS/1000f);
        }

        void Update()
        {
            TimeSlicer.maxExecutionTime = m_BudgetMS / 1000f;
            TimeSlicer.Update(Time.time, Time.unscaledTime);

            if (TimeSlicer.overdueTickCount>0)
            {
                Debug.LogWarning($"TimeSlicer had {TimeSlicer.overdueTickCount} overdue tasks this frame.");
            }
        }

        public CoroutineTimeslice CreateCoroutineTimeslice(MonoBehaviour sourceBehaiour)
        {
            return new CoroutineTimeslice(sourceBehaiour, TimeSlicer);
        }

        public CoroutineTimeslice CreateCoroutineTimeslice(MonoBehaviour sourceBehaiour, float initialTickEstimate)
        {
            return new CoroutineTimeslice(sourceBehaiour, TimeSlicer, initialTickEstimate);
        }

#if UNITY_EDITOR
        [CustomEditor(typeof(TimesliceManager))]
        public class Inspector : Editor
        {
            #region Overrides of Editor
            public override void OnInspectorGUI()
            {
                var manager = target as TimesliceManager;
                if (manager.TimeSlicer!=null)
                {
                    using (new EditorGUI.DisabledScope(true))
                    using (new GUILayout.VerticalScope("box"))
                    {
                        EditorGUILayout.IntField("Scheduled", manager.TimeSlicer.scheduledTickCount);
                        EditorGUILayout.IntField("Overdue", manager.TimeSlicer.overdueTickCount);
                        EditorGUILayout.FloatField("Last Update", manager.TimeSlicer.lastUpdateDuration);
                        EditorGUILayout.FloatField("Average Update", manager.TimeSlicer.averageUpdateDuration);
                    }
                    Repaint();
                }

                base.OnInspectorGUI();
            }
            #endregion
        }
#endif
    }
}