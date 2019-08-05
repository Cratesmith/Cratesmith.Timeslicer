namespace Cratesmith.Timeslicer
{
    public interface ITimeslice
    {
        float addedAtUnscaledTime { get; set; }
        float lastTickDuration { get; set; }
        float minNextTickAtTime { get; }
        float maxNextTickAtTime { get; }
        float tickDurationEstimate { get; }
        int executionOrder { get; }

        bool usesUnscaledTime { get; }

        //bool useEstimateAsDuration              { get; }
        void Tick(float deltaTime, float unscaledDeltaTime);
    }
}