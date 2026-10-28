using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Adds flow arrows (green in, orange out) to every conveyor under this object when the scene starts.
    /// </summary>
    [AddComponentMenu("Conveyor System/Demo/Tutorial/Flow Arrows")]
    public class TutorialFlowArrows : MonoBehaviour
    {
        public Material InMaterial;
        public Material OutMaterial;
        public float Height = 0.56f;

        private void Start()
        {
            foreach (var conveyor in GetComponentsInChildren<ConveyorBase>(true))
                FlowArrows.Add(conveyor.gameObject, conveyor, InMaterial, OutMaterial, Height);
        }
    }
}
