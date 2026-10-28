using System.Collections;
using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Stops a Pauser when the step starts and lets the viewer start / stop it with Space.
    /// The Pauser is started again when the step is left.
    /// </summary>
    [AddComponentMenu("Conveyor System/Demo/Tutorial/Pauser Control")]
    public class TutorialPauserControl : TutorialStepAction
    {
        public PauserConveyor Pauser;
        public bool StopOnEnter = true;

        private bool active;

        public override void Enter()
        {
            active = true;
            if (StopOnEnter && Pauser != null) Pauser.StopConveyor();
        }

        public override void Exit()
        {
            active = false;
            if (Pauser != null) Pauser.StartConveyor();
        }

        private void Update()
        {
            if (active && Pauser != null && DemoInput.GetKeyDown(DemoKey.Space)) Pauser.ToggleConveyor();
        }
    }
}
