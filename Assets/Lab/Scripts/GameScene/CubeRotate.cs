using UnityEngine;
using UnityEngine.UIElements;

public class CubeRotate : MonoBehaviour
{
    [SerializeField] private float speed = 1.0f;
    void Start()
    {
        
    }

    void Update()
    { 
        transform.Rotate(new Vector3(0,speed*Time.deltaTime, 0));
    }
}
