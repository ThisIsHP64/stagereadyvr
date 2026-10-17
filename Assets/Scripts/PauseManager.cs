using UnityEngine;
using UnityEngine.SceneManagement;
using StageReadyVR.Controllers;

public class PauseManager : MonoBehaviour
{
    public Transform centerEyeAnchor;
    public GameObject pauseMenuCanvas;
    private bool isPaused = false;

    void Start()
    {
        if (pauseMenuCanvas != null)
            pauseMenuCanvas.SetActive(false);
    }

    void Update()
    {
        // Menu button to toggle pause
        if (XRControllerInput.GetDown(ControllerButton.LeftMenu))
        {
            if (isPaused) Resume();
            else Pause();
        }

        if (isPaused)
        {
            if (XRControllerInput.GetDown(ControllerButton.RightPrimary)) Resume();           // A
            if (XRControllerInput.GetDown(ControllerButton.RightSecondary)) Restart();      // B
            if (XRControllerInput.GetDown(ControllerButton.LeftPrimary)) ReturnToMainMenu(); // X
        }
    }

    public void Pause()
    {
        isPaused = true;
        if (centerEyeAnchor != null)
        {
            pauseMenuCanvas.transform.position = centerEyeAnchor.position + (centerEyeAnchor.forward * 1.2f);
            Vector3 lookTarget = centerEyeAnchor.position;
            lookTarget.y = pauseMenuCanvas.transform.position.y;
            pauseMenuCanvas.transform.LookAt(lookTarget);
            pauseMenuCanvas.transform.Rotate(0, 180, 0);
        }
        pauseMenuCanvas.SetActive(true);
    }

    public void Resume()
    {
        isPaused = false;
        pauseMenuCanvas.SetActive(false);
    }

    public void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ReturnToMainMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }
}