/*
* Voicechat for Unity3D + Mirror.
* 
* https://unity.com/
* https://mirror-networking.com/
*/

/*
* VoiceChat.cs by Pelle "Stupid++" Bruinsma
* 
* To the extent possible under law, the person who associated CC0 with
* VoiceChat.cs has waived all copyright and related or neighboring rights
* to VoiceChat.cs.
* 
* You should have received a copy of the CC0 legalcode along with this
* work.  If not, see http://creativecommons.org/publicdomain/zero/1.0/.
*/

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mirror;
using UnityEngine.UI;

[RequireComponent(typeof(AudioSource))]
public class VoiceChat : NetworkBehaviour
{
    public KeyCode voiceChatKey = KeyCode.V;
    public bool previewVoice = true;

    [Header("UI")]
    public Image localVCIcon;
    public Image nonLocalVCIcon;

    [Header("Misc settings")]
    [Range(11025, 48000)]
    public int sampleRate = 22050;
    public int chunkSize = 256;
    public int deviceIndex = -1;
    public VCPlaybackMode mode = VCPlaybackMode.Clip;

    private bool speaking;
    private List<VoicePacket> packetQueue = new List<VoicePacket>();
    private AudioClip microphoneClip;
    private int lastMicReadPos = 0;
    private string device;

    private int playbackSampleRate;
    private float playbackPos = 0;
    private AudioSource source;

    private void Start()
    {
        //get samplerate for playback
        playbackSampleRate = AudioSettings.outputSampleRate;

        //start playback source
        source = GetComponent<AudioSource>();

        source.loop = true;
        source.clip = AudioClip.Create("VoiceChat", sampleRate, 1, sampleRate, true, OnAudioRead, OnAudioSetPosition);
        source.Play();

        if (isLocalPlayer)
        {
            //start mic
            device = (deviceIndex < 0) ? "" : Microphone.devices[deviceIndex];

            microphoneClip = Microphone.Start(device, true, 1, sampleRate);
            lastMicReadPos = Microphone.GetPosition(device);
        }
    }

    private void Update()
    {
        if (isLocalPlayer)
        {
            int micReadPos = Microphone.GetPosition(device); //read pos
            int sampleDiff = (micReadPos < lastMicReadPos) ? //get the difference in audio samples
                microphoneClip.samples - lastMicReadPos + micReadPos
                :
                micReadPos - lastMicReadPos
                ;

            int packetDiff = Mathf.FloorToInt((float)sampleDiff / (float)chunkSize); //find number of packets in sample diff

            if (Input.GetKey(voiceChatKey))
            {
                for (int p = 0; p < packetDiff; p++) //send packets
                {
                    VoicePacket packet = new VoicePacket(GetDataFromMic(chunkSize, lastMicReadPos + (p * chunkSize)));

                    SendPacket(packet);
                }
            }

            lastMicReadPos += packetDiff * chunkSize; //advance lastReadPos by how many packets we made

            //do ui and stuff
            speaking = Input.GetKey(voiceChatKey);

            if (nonLocalVCIcon != null)
            {
                nonLocalVCIcon.gameObject.SetActive(false);
            }

            if(localVCIcon != null)
            {
                localVCIcon.gameObject.SetActive(speaking);
            }
        }
        else
        {
            //do ui and stuff
            speaking = packetQueue.Count > 0;

            if (nonLocalVCIcon != null)
            {
                nonLocalVCIcon.gameObject.SetActive(speaking);
            }
        }
    }

    private float[] GetDataFromMic(int length, int offset)
    {
        float[] micData = new float[microphoneClip.samples];
        microphoneClip.GetData(micData, 0);

        float[] data = new float[length];

        for(int d = 0; d < data.Length; d++)
        {
            data[d] = micData[Mathf.FloorToInt(Mathf.Repeat(d + offset, micData.Length))];
        }

        return data;
    }

    #region Networking

    private void SendPacket(VoicePacket packet)
    {
        if (previewVoice)
        {
            packetQueue.Add(packet);
        }

        CmdSendData(NetworkTools.ObjectToData(packet));
    }

    [Command]
    private void CmdSendData(byte[] message)
    {
        ClientReceiveData(message);
    }

    [ClientRpc]
    private void ClientReceiveData(byte[] message)
    {
        if (!isLocalPlayer)
        {
            VoicePacket serializedMessage = null;

            try
            {
                serializedMessage = (VoicePacket)NetworkTools.DataToObject(message);
            }
            catch (System.Exception ex)
            {
                Debug.LogError(ex.ToString());
            }

            if (serializedMessage != null)
            {
                packetQueue.Add(serializedMessage);
            }
        }
    }

    #endregion

    #region Playback

    private void OnAudioRead(float[] data)
    {
        if (mode != VCPlaybackMode.Clip)
        {
            return;
        }

        float sample = 0;

        for (int d = 0; d < data.Length; d++)
        {
            if (packetQueue.Count <= 0)
            {
                //sweet, sweet silence
                sample = 0;
            }
            else
            {
                sample = packetQueue[0].chunk[Mathf.FloorToInt(playbackPos)];

                //advance playbackpos and switch packet when neccesary
                playbackPos++;
                if (playbackPos >= packetQueue[0].chunk.Length)
                {
                    playbackPos = 0;
                    packetQueue.RemoveAt(0);
                }
            }

            data[d] = sample;
        }
    }

    void OnAudioSetPosition(int newPosition)
    {
        //Don't really need it.
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (mode != VCPlaybackMode.Effect)
        {
            return;
        }

        float sampleRateRatio = (float)sampleRate / (float)playbackSampleRate;
        float sample = 0;

        for (int d = 0; d < data.Length; d++)
        {
            if(d % channels == 0)
            {
                if (packetQueue.Count <= 0)
                {
                    //sweet, sweet silence
                    sample = 0;
                }
                else
                {
                    sample = packetQueue[0].chunk[Mathf.FloorToInt(playbackPos)];

                    //advance playbackpos and switch packet when neccesary
                    playbackPos += sampleRateRatio;
                    if (playbackPos >= packetQueue[0].chunk.Length)
                    {
                        playbackPos = 0;
                        packetQueue.RemoveAt(0);
                    }
                }
            }

            data[d] = sample;
        }
    }

    #endregion 
}

public enum VCPlaybackMode
{
    Effect, //uses onaudiofilterread
    Clip //uses clip callbacks
}

[System.Serializable]
public class VoicePacket
{
    public float[] chunk;

    public VoicePacket(float[] chunk)
    {
        this.chunk = chunk;
    }
}

public static class NetworkTools
{
    public static byte[] ObjectToData(object msg)
    {
        System.Runtime.Serialization.Formatters.Binary.BinaryFormatter bf = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
        using (var ms = new System.IO.MemoryStream())
        {
            bf.Serialize(ms, (object)msg);
            return ms.ToArray();
        }
    }

    public static object DataToObject(byte[] data)
    {
        using (var memStream = new System.IO.MemoryStream())
        {
            var binForm = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
            memStream.Write(data, 0, data.Length);
            memStream.Seek(0, System.IO.SeekOrigin.Begin);
            var obj = binForm.Deserialize(memStream);

            try
            {
                return obj;
            }
            catch
            {
                Debug.LogWarning("Could not convert data to message.");
                return null;
            }
        }
    }
}