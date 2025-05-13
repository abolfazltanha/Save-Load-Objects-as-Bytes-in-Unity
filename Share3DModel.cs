using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

public class Share3DModel : MonoBehaviour
{
    public Save_Load_Mesh Save_Load_Mesh;

    private const byte EventCode = 124;
    private const int ChunkSize = 50000; // 50KB
    private const float ChunkSendDelay = 0.02f;

    private Queue<byte[]> sendQueue = new Queue<byte[]>();
    private bool isSending = false;

    private byte[] receivedData;
    private int receivedBytesCount = 0;

    private void OnEnable()
    {
        PhotonNetwork.NetworkingClient.EventReceived += OnEvent;
    }

    private void OnDisable()
    {
        PhotonNetwork.NetworkingClient.EventReceived -= OnEvent;
    }

    [ContextMenu("SendModelTest")]
    public void ShareModel()
    {
        string modelData = PlayerPrefs.GetString("FullObject");
        if (string.IsNullOrEmpty(modelData))
        {
            Debug.LogWarning("No Model for Sending");
            return;
        }

        byte[] modelBytes = Encoding.UTF8.GetBytes(modelData);
        byte[] compressedBytes = Compress(modelBytes);

        Debug.Log($"Original size: {modelBytes.Length} bytes, Compressed size: {compressedBytes.Length} bytes");

        int totalLength = compressedBytes.Length;

        for (int i = 0; i < totalLength; i += ChunkSize)
        {
            int currentChunkSize = Mathf.Min(ChunkSize, totalLength - i);
            byte[] chunk = new byte[currentChunkSize];
            System.Array.Copy(compressedBytes, i, chunk, 0, currentChunkSize);

            bool isFirst = (i == 0);
            bool isLast = (i + currentChunkSize) >= totalLength;

            object[] content = new object[] { chunk, totalLength, isFirst, isLast };
            sendQueue.Enqueue(SerializeContent(content));
        }

        if (!isSending)
        {
            StartCoroutine(SendChunks());
        }
    }

    private IEnumerator SendChunks()
    {
        isSending = true;

        while (sendQueue.Count > 0)
        {
            byte[] data = sendQueue.Dequeue();

            PhotonNetwork.RaiseEvent(
                EventCode,
                data,
                new RaiseEventOptions { Receivers = ReceiverGroup.All },
                new SendOptions { Reliability = true }
            );

            yield return new WaitForSeconds(ChunkSendDelay);
        }

        isSending = false;
    }

    private void OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code == EventCode)
        {
            byte[] data = (byte[])photonEvent.CustomData;
            var content = DeserializeContent(data);

            byte[] chunk = (byte[])content[0];
            int totalLength = (int)content[1];
            bool isFirst = (bool)content[2];
            bool isLast = (bool)content[3];

            if (isFirst)
            {
                receivedData = new byte[totalLength];
                receivedBytesCount = 0;
            }

            System.Array.Copy(chunk, 0, receivedData, receivedBytesCount, chunk.Length);
            receivedBytesCount += chunk.Length;

            if (isLast)
            {
                byte[] decompressedBytes = Decompress(receivedData);
                string modelString = Encoding.UTF8.GetString(decompressedBytes);

                Debug.Log($"3D model has been generated successfully: {modelString.Length}");

                Save_Load_Mesh.LoadedObject(modelString);

                receivedData = null;
                receivedBytesCount = 0;
            }
        }
    }

    private static byte[] Compress(byte[] data)
    {
        using (var output = new MemoryStream())
        {
            using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
            {
                gzip.Write(data, 0, data.Length);
            }
            return output.ToArray();
        }
    }

    private static byte[] Decompress(byte[] data)
    {
        using (var input = new MemoryStream(data))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            gzip.CopyTo(output);
            return output.ToArray();
        }
    }

    private static byte[] SerializeContent(object[] content)
    {
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            byte[] chunk = (byte[])content[0];
            writer.Write(chunk.Length);
            writer.Write(chunk);
            writer.Write((int)content[1]);
            writer.Write((bool)content[2]);
            writer.Write((bool)content[3]);
            return ms.ToArray();
        }
    }

    private static object[] DeserializeContent(byte[] data)
    {
        using (var ms = new MemoryStream(data))
        using (var reader = new BinaryReader(ms))
        {
            int chunkLength = reader.ReadInt32();
            byte[] chunk = reader.ReadBytes(chunkLength);
            int totalLength = reader.ReadInt32();
            bool isFirst = reader.ReadBoolean();
            bool isLast = reader.ReadBoolean();
            return new object[] { chunk, totalLength, isFirst, isLast };
        }
    }
}
