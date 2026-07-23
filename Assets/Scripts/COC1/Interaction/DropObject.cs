using UnityEngine;

public class DropObject : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q))
        {
            Transform holdPoint = transform.Find("HoldPoint");

            if (holdPoint.childCount > 0)
            {
                Transform obj = holdPoint.GetChild(0);

                obj.SetParent(null);

                Rigidbody rb = obj.GetComponent<Rigidbody>();
                if (rb != null)
                    rb.isKinematic = false;
            }
        }
    }
}