using UnityEngine;
using UnityEngine.SceneManagement;

public class StoryMode : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    SceneManager sceneManager;
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    public void OpenStoryMode() 
    {
        SceneManager.LoadScene("COC1-StoryMode");
    }
}
