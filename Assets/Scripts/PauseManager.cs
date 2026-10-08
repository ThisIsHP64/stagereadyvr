using UnityEngine;
using UnityEngine.SceneManagement;

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
        if (OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.LTouch))
        {
            if (isPaused) Resume();
            else Pause();
        }

        if (isPaused)
        {
            if (OVRInput.GetDown(OVRInput.Button.One)) Resume();           // A
            if (OVRInput.GetDown(OVRInput.Button.Two)) Restart();          // B
            if (OVRInput.GetDown(OVRInput.Button.Three)) ReturnToMainMenu(); // X
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