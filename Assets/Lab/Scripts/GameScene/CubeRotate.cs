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
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");
        Vector3 movement = new Vector3(x, 0, z);
        transform.Translate(movement.normalized * speed * Time.deltaTime);
    }
}
