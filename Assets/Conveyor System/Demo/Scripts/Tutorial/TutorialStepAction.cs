using System.Collections;
using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Something a tutorial step starts when it is shown and stops when it is left.
    /// </summary>
    public abstract class TutorialStepAction : MonoBehaviour
    {
        /// <summary>
        /// Called once when the tutorial starts, before any step is shown.
        /// </summary>
        public virtual void Prepare() { }

        /// <summary>
        /// Called when the step is shown.
        /// </summary>
        public virtual void Enter() { }

        /// <summary>
        /// Called when the step is left.
        /// </summary>
        public virtual void Exit() { }
    }
}
