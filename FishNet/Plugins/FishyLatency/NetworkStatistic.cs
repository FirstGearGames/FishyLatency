using BeardedMonkeys;
using FishNet;
using UnityEngine;

public class NetworkStatistic : MonoBehaviour
{
    [Tooltip("The transport which contains the calculation.")]
    [SerializeField]
    private FishyLatency _transport;

    private GUIStyle _style = new GUIStyle();
    private GUIStyle _headerStyle = new GUIStyle();
    private float? _lastVertical;
    private bool _lastServer;
    private bool _lastClient;
    private int _lastScreenWidth;
    private int _lastScreenHeight;

    private void OnGUI()
    {
        if (_transport == null)
            return;

        bool isServer = InstanceFinder.IsServer;
        bool isClient = InstanceFinder.IsClient;
        if (_lastServer != isServer || _lastClient != isClient)
        {
            _lastServer = isServer;
            _lastClient = isClient;
            _lastVertical = null;
        }
        else if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight)
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            _lastVertical= null;
        }

        Vector2 multiplier = new Vector2(Screen.width / 1920f, Screen.height / 1080f);
        _style.normal.textColor = Color.black;
        _style.fontSize = (int)(35 * multiplier.y);
        _style.fontStyle = FontStyle.Normal;

        _headerStyle.normal.textColor = Color.black;
        _headerStyle.fontSize = (int)(50 * multiplier.y);
        _headerStyle.fontStyle = FontStyle.Bold;

        float width = 85f * multiplier.x;
        float height = 15f + multiplier.y;

        float horizontal = 10f;
        /* An incredibly lazy way to calculate how far
         * stuff needs to be drawn from the bottom. */
        float vertical = (_lastVertical == null) ? 0 : (Screen.height - _lastVertical.Value - 10f);

        if (InstanceFinder.IsServer)
        {
            GUI.Label(new Rect(horizontal, vertical, width, height), $"Server", _headerStyle);
            vertical += _headerStyle.fontSize;
            GUI.Label(new Rect(horizontal, vertical, width, height), $"Received Packets: {_transport.ReceivedPacketsServer}/s", _style);
            vertical += _style.fontSize;
            GUI.Label(new Rect(horizontal, vertical, width, height), $"Received Bytes: {_transport.ReceivedBytesServer}/s", _style);
            vertical += _style.fontSize;
            GUI.Label(new Rect(horizontal, vertical, width, height), $"Sent Packets: {_transport.SentPacketsServer}/s", _style);
            vertical += _style.fontSize;
            GUI.Label(new Rect(horizontal, vertical, width, height), $"Sent Bytes: {_transport.SentBytesServer}/s", _style);
            vertical += _style.fontSize;
        }
        if (InstanceFinder.IsClient)
        {
            GUI.Label(new Rect(horizontal, vertical, width, height), $"Client", _headerStyle);
            vertical += _headerStyle.fontSize;
            GUI.Label(new Rect(horizontal, vertical, width, height), $"Received Packets: {_transport.ReceivedPacketsClient}/s", _style);
            vertical += _style.fontSize;
            GUI.Label(new Rect(horizontal, vertical, width, height), $"Received Bytes: {_transport.ReceivedBytesClient}/s", _style);
            vertical += _style.fontSize;
            GUI.Label(new Rect(horizontal, vertical, width, height), $"Sent Packets: {_transport.SentPacketsClient}/s", _style);
            vertical += _style.fontSize;
            GUI.Label(new Rect(horizontal, vertical, width, height), $"Sent Bytes: {_transport.SentBytesClient}/s", _style);
            vertical += _style.fontSize;
        }

        if (_lastVertical == null)
            _lastVertical = vertical;
    }
}
