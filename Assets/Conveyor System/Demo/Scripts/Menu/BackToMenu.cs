using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Returns to the menu scene when M is pressed.
    /// </summary>
    [AddComponentMenu("Conveyor System/Demo/Back To Menu")]
    public class BackToMenu : MonoBehaviour
    {
        private void Update()
        {
            if (DemoInput.GetKeyDown(DemoKey.M)) SceneLoader.LoadMenu();
        }
    }
}
