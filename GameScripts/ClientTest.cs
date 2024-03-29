using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

public class Client : MonoBehaviour
{
    private IPAddress hostIP;
    private int serverPORT;

    private TcpClient client;
    private NetworkStream serverStream;

    private int receiveTimeout;
    private string handshake_start_message;
    private string model_successful_fit_message;

    private int model_layers;
    private int batch_size;
    private int epochs;
    private double model_learning_rate;
    private int model_dataset_complexity;
    private List<int> layer_neurons;

    private const int common_neurons_per_layer = 16;

    private EGameStatus current_game_status;

    [SerializeField]
    public GameObject player;

    [SerializeField]
    public GameObject button;

    private Transform player_transform;
    private float speed;

    private void Awake()
    {
        current_game_status = EGameStatus.prepareToGame;

        hostIP = IPAddress.Parse(""); //need a host ip
        serverPORT = 40200;

        client = null;
        serverStream = null;
    }
    private void Start()
    {
        receiveTimeout = 20000;

        handshake_start_message = "Hello";
        model_successful_fit_message = "Complete";

        model_layers = 2;
        batch_size = 8;
        epochs = 30;
        model_learning_rate = 0.0001;
        model_dataset_complexity = 0;

        layer_neurons = new List<int>() { common_neurons_per_layer, common_neurons_per_layer };

        player_transform = player.GetComponent<Transform>();
        speed = 2f;
    }
    private enum EHandshakeStatus
    {
        Complete,
        Failed
    }
    private enum EDataExchangeStatus
    { 
        Ok,
        Bad
    }
    private enum EGameStatus
    {
        gameOn,
        prepareToGame,
        getNewAction,
        badEnd
    }
    private byte[] ReadStreamData()
    {
        try
        {
            if (client != null && serverStream != null)
            {

                if (client.Connected)
                {
                    byte[] buffer = new byte[4];
                    serverStream.Read(buffer, 0, buffer.Length);
                    Array.Reverse(buffer);

                    int messageLength = BitConverter.ToInt32(buffer, 0);
                    buffer = new byte[messageLength];
                    int bytesReaded = 0;

                    while (bytesReaded < messageLength)
                        bytesReaded += serverStream.Read(buffer, bytesReaded, messageLength - bytesReaded);

                    return buffer;
                }
            }
        }
        catch (Exception e)
        {
            Debug.Log(e);
        }

        return null;
    }
    private EHandshakeStatus DoHandshake()
    {
        try
        {
            if (client != null && serverStream != null)
            {
                if (client.Connected)
                {
                    byte[] handshake_start_message_bytes = Encoding.UTF8.GetBytes(handshake_start_message);
                    byte[] handshake_start_message_bytes_length = BitConverter.GetBytes(handshake_start_message_bytes.Length);

                    serverStream.Write(handshake_start_message_bytes_length, 0, 4);
                    serverStream.Write(handshake_start_message_bytes, 0, handshake_start_message_bytes.Length);

                    byte[] readed_data = ReadStreamData();
                    string message = Encoding.UTF8.GetString(readed_data);

                    if (message == handshake_start_message)
                        return EHandshakeStatus.Complete;
                }
            }

            throw new Exception("Error during handshake!");
        }
        catch
        {
            return EHandshakeStatus.Failed;
        }
    }
    private EDataExchangeStatus WaitUntilModelTrained()
    {
        try
        {
            if (client != null && serverStream != null)
            {
                if (client.Connected)
                {
                    byte[] readed_data = ReadStreamData();
                    string message = Encoding.UTF8.GetString(readed_data);

                    if (message == model_successful_fit_message)
                        return EDataExchangeStatus.Ok;
                }
            }
        }
        catch (Exception e)
        {
            Debug.Log(e);

            return EDataExchangeStatus.Bad;
        }

        return EDataExchangeStatus.Bad;
    }
    private byte[] ConvertToByteArray(object obj)
    {
        BinaryFormatter fm = new BinaryFormatter();
        using (MemoryStream ms = new MemoryStream())
        {
            fm.Serialize(ms, obj);
            return ms.ToArray();
        }
    }
    private EDataExchangeStatus SendModelArchitecture()
    {
        List<double> modelArchitecture_params = new List<double>() { model_layers, batch_size, epochs, model_learning_rate, model_dataset_complexity };

        try
        {
            byte[] encodedModelArchitecture = ConvertToByteArray(modelArchitecture_params);
            byte[] encodedModelArchitecture_length = BitConverter.GetBytes(encodedModelArchitecture.Length);

            serverStream.Write(encodedModelArchitecture_length, 0, 4);
            serverStream.Write(encodedModelArchitecture, 0, encodedModelArchitecture.Length);

            byte[] encodedLayerNeurons = ConvertToByteArray(layer_neurons);
            byte[] encodedLayerNeurons_length = BitConverter.GetBytes(encodedLayerNeurons.Length);

            serverStream.Write(encodedLayerNeurons_length, 0, 4);
            serverStream.Write(encodedLayerNeurons, 0, encodedLayerNeurons.Length);
        }
        catch
        {
            return EDataExchangeStatus.Bad;
        }

        return EDataExchangeStatus.Ok;
    }
    private EDataExchangeStatus SendCurrentDataToPredictNewAction()
    {
        List<double> currentDataToPredict = new List<double>() { player_transform.position.x, player_transform.position.y, 2f, 2f };

        try
        {
            byte[] encodedCurrentDataToPredict = ConvertToByteArray(currentDataToPredict);
            byte[] encodedCurrentDataToPredict_length = BitConverter.GetBytes(encodedCurrentDataToPredict.Length);

            serverStream.Write(encodedCurrentDataToPredict_length, 0, 4);
            serverStream.Write(encodedCurrentDataToPredict, 0, encodedCurrentDataToPredict.Length);
        }
        catch
        {
            return EDataExchangeStatus.Bad;
        }

        return EDataExchangeStatus.Ok;
    }
    private List<float> GetNewPredictedAction()
    {
        try
        {
            EDataExchangeStatus SendCurrentDataToPredictNewAction_status = SendCurrentDataToPredictNewAction();
            if (SendCurrentDataToPredictNewAction_status == EDataExchangeStatus.Ok)
            {
                List<float> predict = new List<float>();

                for (int i = 0; i < 3; i++)
                {
                    byte[] readed_data = ReadStreamData();
                    float numb = float.Parse(Encoding.UTF8.GetString(readed_data).Replace(".", ","));

                    predict.Add(numb);
                }

                return predict;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
    public async void OnClickConnectButton()
    {
        try
        {
            client = new TcpClient();
            client.ReceiveTimeout = receiveTimeout;

            await client.ConnectAsync(hostIP, serverPORT);
            if (client.Connected == true)
            {
                serverStream = client.GetStream();

                EHandshakeStatus handshakeStatus = DoHandshake();
                if (handshakeStatus == EHandshakeStatus.Complete)
                {
                    EDataExchangeStatus sendModelArchitecture_status = SendModelArchitecture();
                    if (sendModelArchitecture_status == EDataExchangeStatus.Ok)
                    {
                        EDataExchangeStatus getModelTrained_status = WaitUntilModelTrained();
                        if (getModelTrained_status == EDataExchangeStatus.Ok)
                        {
                            button.SetActive(false);
                            current_game_status = EGameStatus.gameOn;
                        }
                    }
                }
            } 
        }
        catch (Exception e)
        {
            Debug.Log(e);
        }
    }
    private void Update()
    {
        if (current_game_status == EGameStatus.gameOn)
        {
            current_game_status = EGameStatus.getNewAction;

            try
            {
                if (client != null & serverStream != null)
                {
                    if (client.Connected == true)
                    {
                        List<float> new_action = GetNewPredictedAction();
                        if (new_action != null)
                        {
                            float move_u = new_action[0];
                            float move_d = new_action[1];
                            float move_r = new_action[2];

                            player_transform.Translate(speed * Time.deltaTime, 0f, 0f);
                            player_transform.Translate(0f, move_u * speed * Time.deltaTime, 0f);
                            player_transform.Translate(0f, move_d * -speed * Time.deltaTime, 0f);
                        }

                        current_game_status = EGameStatus.gameOn;
                    }
                    else
                        throw new Exception("Error during game process!");
                }
                else
                    throw new Exception("Error during game process!");
            }
            catch 
            {
                current_game_status = EGameStatus.badEnd;
            }
        }
    }
}