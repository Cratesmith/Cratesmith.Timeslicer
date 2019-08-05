using System;
using System.Diagnostics;
using Cratesmith.ScriptExecutionOrder;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Cratesmith.Timeslicer
{
public class CoroutineTimeslice : ITimeslice, IDisposable
{
    public class YieldInstruction : CustomYieldInstruction
    {
        internal bool m_keepWaiting = true;
        public override bool keepWaiting
        {
            get { return m_keepWaiting; }
        }
    }
    private YieldInstruction m_yieldInstruction = new YieldInstruction();

    private readonly Stopwatch m_stopwatch = new Stopwatch();
    private bool m_isRunning = false;
    private float m_minDuration = 0f;
    private float m_maxDuration = 0f;
    private float m_lastExecutionTime = 0f;
    private int m_executionOrder = 0;
    private bool m_usesUnscaledTime = true;
    private TimeSlicer m_timeSlicer;
    private float m_lastTickEndTime;

    float ITimeslice.addedAtUnscaledTime    { get; set; }
    float ITimeslice.lastTickDuration       { get; set; }
    float ITimeslice.minNextTickAtTime      { get { return m_minDuration+((ITimeslice)this).addedAtUnscaledTime; } }
    float ITimeslice.maxNextTickAtTime      { get { return m_maxDuration+((ITimeslice)this).addedAtUnscaledTime; } }
    float ITimeslice.tickDurationEstimate   { get { return m_lastExecutionTime; } }
    int ITimeslice.executionOrder           { get { return m_executionOrder; } }
    bool ITimeslice.usesUnscaledTime        { get { return m_usesUnscaledTime; } }

    public float elapsedTime { get {return m_lastExecutionTime; } set { m_lastExecutionTime = value; } }
    public float deltaTime => Time.time - m_lastTickEndTime;

    void ITimeslice.Tick(float deltaTime, float unscaledDeltaTime)
    {
        m_yieldInstruction.m_keepWaiting = false;
    }

    public CoroutineTimeslice(int executionOrder=0)
    {
        m_executionOrder = executionOrder;
    }

    public CoroutineTimeslice(MonoBehaviour hostBehaviour, TimeSlicer timeslicer, float initialTickDurationEstimate=0.02f)
    {
        m_lastExecutionTime = initialTickDurationEstimate;
        m_executionOrder = ScriptExecutionOrderCache.GetExecutionOrder(hostBehaviour.GetType());
        m_timeSlicer = timeslicer;
    }

    ~CoroutineTimeslice()
    {
        m_timeSlicer?.Remove(this);
    }

    public CustomYieldInstruction EndAndWait(float minDuration, float maxDuration, bool useUnscaledTime=false)
    {
        End();        
        m_minDuration = minDuration;
        m_maxDuration = maxDuration;
        m_usesUnscaledTime = useUnscaledTime;
        m_yieldInstruction.m_keepWaiting = true;
        m_timeSlicer.Add(this);
        return m_yieldInstruction;
    }

    public CoroutineTimeslice Begin()
    {
        End();
        m_isRunning = true;
        m_stopwatch.Reset();
        m_stopwatch.Start();
        return this;
    }

    public void End()
    {
        if (!m_isRunning)
        {
            return;
        }

        m_isRunning = false;

        m_lastExecutionTime = (m_lastExecutionTime*3+(float)m_stopwatch.Elapsed.TotalSeconds)/4f;
        m_lastTickEndTime = Time.time;
        m_stopwatch.Stop();
    }

    #region Implementation of IDisposable

    public void Dispose()
    {
        End();
    }

    #endregion
}       
}
