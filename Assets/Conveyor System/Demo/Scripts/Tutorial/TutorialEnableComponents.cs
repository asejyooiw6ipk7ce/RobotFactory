using System.Collections;
using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Enables behaviours (e.g. PlayerInteraction) only while the step is shown.
    /// </summary>
    [AddComponentMenu("Conveyor System/Demo/Tutorial/Enable Components")]
    public class TutorialEnableComponents : TutorialStepAction
    {
        public Behaviour[] Targets;

        public override void Prepare() => Set(false);
        public override void Enter() => Set(true);
        public override void Exit() => Set(false);

        private void Set(bool enabled)
        {
            if (Targets == null) return;
            foreach (var b in Targets)
                if (b != null) b.enabled = enabled;
        }
    }
}
