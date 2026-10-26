namespace VPet_Simulator.Core;

/// <summary>Optional host clock for continuous translation; configured speeds remain per legacy interval.</summary>
public interface IContinuousMovementController
{
    void StartContinuousMove(double xPerInterval, double yPerInterval, double intervalMilliseconds);
    void StopContinuousMove();
}
