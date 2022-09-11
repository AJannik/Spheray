using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private float moveSpeed;
    [SerializeField] private float rotationSpeed;
    
    private void Update()
    {
        transform.position += transform.right * (Input.GetAxis("Horizontal") * Time.deltaTime * moveSpeed);
        transform.position += transform.forward * (Input.GetAxis("Vertical") * Time.deltaTime * moveSpeed);

        if (Input.GetMouseButton(1))
        {
            transform.eulerAngles += Quaternion.Euler(Vector3.up * (Input.GetAxis("Mouse X") * Time.deltaTime * rotationSpeed * Mathf.Rad2Deg)).eulerAngles;
            transform.Rotate(Vector3.right * (-Input.GetAxis("Mouse Y") * Time.deltaTime * rotationSpeed * Mathf.Rad2Deg));
            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Input.GetKey(KeyCode.LeftShift))
        {
            transform.position += transform.up * (Time.deltaTime * moveSpeed);
        }

        if (Input.GetKey(KeyCode.LeftControl))
        {
            transform.position -= transform.up * (Time.deltaTime * moveSpeed);
        }
    }
}
