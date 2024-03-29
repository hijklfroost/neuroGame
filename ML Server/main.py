#About:
#En: This project is under active development many features have not yet been implemented.
#Ru: Этот проект находится в активной разработке, многие возможности пока не реализованы.

#TODO:
#En: Implement a config file and read important constants from it.
#Ru: Реализовать конфиг файл и считывание важных констант из него.

import socket
import struct
import tensorflow as tf
import json
import numpy as np

import clr
clr.AddReference("Python.Runtime")
clr.AddReference("System.IO")
clr.AddReference("System.Runtime.Serialization.Formatters")
clr.AddReference("System.Collections")

from System.IO import MemoryStream #type: ignore
from System.Runtime.Serialization.Formatters.Binary import BinaryFormatter #type: ignore
from System.Collections.Generic import List #type: ignore
from System import Double #type: ignore

from time import sleep

HOST_ADDR = "" #local host ip address
PORT = 40200

HANDSHAKE_START_MESSAGE = "Hello"
MODEL_SUCCESSFUL_FIT_MESSAGE = "Complete"

DATASETS_PATH = "Datasets"
DATASET_COMPLEXITIES = {0: "Easy", 1: "Medium", 2: "Hard"}

INPUT_NEURONS = 4
OUTPUT_NEURONS = 3

def ReadConfigFile():
    pass

def DoHandshake(conn: socket.socket) -> int:
    startMessage = HANDSHAKE_START_MESSAGE.encode()      
    
    try:
        client_startMessage_lengthBytes = conn.recv(4)
        client_startMessage_length = struct.unpack("<I", client_startMessage_lengthBytes)[0]
            
        client_startMessage = conn.recv(client_startMessage_length).decode()
        if client_startMessage == HANDSHAKE_START_MESSAGE:
            conn.sendall(struct.pack("!I", len(startMessage)))
            conn.sendall(startMessage)
            
            return 1
    except:
        pass     
        
    return -1

def SendModelSuccessfulFitCompleteMessage(conn: socket.socket) -> int:
    message = MODEL_SUCCESSFUL_FIT_MESSAGE.encode()      
    
    try:
        conn.sendall(struct.pack("!I", len(message)))
        conn.sendall(message)
    except:
        pass     
    else:
        return 1
        
    return -1

def GetModelArhitecture(conn: socket.socket) -> dict:
    modelArchitecture = {"layers": 0, "batch_size": 0, "epochs": 0, "learning_rate": 0, "layer_neurons": [], "dataset_complexity": 0}
    
    try:
    
        bytes_modelLength = conn.recv(4)
        modelLength = struct.unpack("<I", bytes_modelLength)[0]
        
        modelBytes = conn.recv(modelLength)
        if modelBytes:
            
            fm = BinaryFormatter()
            ms = MemoryStream(modelBytes)
        
            model = fm.Deserialize(ms)
            modelParams = [i for i in model]
            
            modelArchitecture["layers"] = int(modelParams[0])
            modelArchitecture["batch_size"] = int(modelParams[1])
            modelArchitecture["epochs"] = int(modelParams[2])
            modelArchitecture["learning_rate"] = float(modelParams[3])
            modelArchitecture["dataset_complexity"] = int(modelParams[4])
            
            ms.Close()
            
        bytes_layerNeuronsLength = conn.recv(4)
        layerNeuronsLength = struct.unpack("<I", bytes_layerNeuronsLength)[0]
        
        layerNeurons_Serialized = conn.recv(layerNeuronsLength)
        if layerNeurons_Serialized:
            
            fm = BinaryFormatter()
            ms = MemoryStream(layerNeurons_Serialized)
        
            layerNeurons = fm.Deserialize(ms)
            modelArchitecture["layer_neurons"] = [i for i in layerNeurons]
            
            ms.Close()
            
    except:
        return None
    else:
        return modelArchitecture

def CreateModel(layers: int, layer_neurons: list, learning_rate: float):
    model = tf.keras.Sequential()
    model.add(tf.keras.layers.InputLayer(input_shape=(INPUT_NEURONS,)))
    
    for i in range(layers):
        model.add(tf.keras.layers.BatchNormalization())
        model.add(tf.keras.layers.Dense(layer_neurons[i], activation='relu'))
    
    model.add(tf.keras.layers.Dropout(0.5)) 
    model.add(tf.keras.layers.Dense(OUTPUT_NEURONS, activation='softmax'))
    
    model.compile(optimizer=tf.keras.optimizers.Adam(learning_rate=learning_rate),
                  loss='categorical_crossentropy',
                  metrics=['accuracy'])
    
    return model

def LoadDataset(path: str) -> dict:
    inputs_data = []
    outputs_data = []
    
    with open(f"{path}\\data.json", "r", encoding="utf-8") as f:
        json_obj = json.load(f)
        train_data = json_obj["train_data"]
        
        for row in train_data:
            inputs_data.append(row["input"])
            outputs_data.append(row["output"])
            
    return {"input_data": np.array(inputs_data), "output_data": np.array(outputs_data)}

def FitModel(model: tf.keras.Sequential, epochs: int, batch_size: int, input_data, output_data):
    model.fit(x=input_data, y=output_data, epochs=epochs, batch_size=batch_size, validation_split=0.2)
    return model

def GetDataToPredictNewAction(conn: socket.socket):
    try:
        data_bytesLength = conn.recv(4)
        dataLength = struct.unpack("<I", data_bytesLength)[0]
        
        data_Serialized = conn.recv(dataLength)
        if data_Serialized:
            
            fm = BinaryFormatter()
            ms = MemoryStream(data_Serialized)
            
            data_Deserialized = fm.Deserialize(ms)
            data = np.array([[i for i in data_Deserialized]])

            ms.Close()
            
            return data
    except:
        return None

def PredictNewAction(model: tf.keras.Sequential, input_data):
    predict = model.predict(input_data)
    return [float(predict[0, 0]), float(predict[0, 1]), float(predict[0, 2])]

def SendNewAction(conn: socket.socket, actions_list: list):
    for action in actions_list:
        numb = str(round(action, 2)).encode()
        
        conn.sendall(struct.pack("!I", len(numb)))
        conn.sendall(numb)

def main():
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind((HOST_ADDR, PORT))
        sock.listen()
        
        conn, addr = sock.accept()
        with conn:
            
            handshakeStatus = DoHandshake(conn=conn)
            if handshakeStatus == 1:
                
                modelArchitecture = GetModelArhitecture(conn=conn)
                
                layers = modelArchitecture["layers"]
                layer_neurons = modelArchitecture["layer_neurons"]
                batch_size = modelArchitecture["batch_size"]
                epochs = modelArchitecture["epochs"]
                learning_rate = modelArchitecture["learning_rate"]
                dataset_complexity = modelArchitecture["dataset_complexity"]

                dataset_path = f"{DATASETS_PATH}\\{DATASET_COMPLEXITIES[dataset_complexity]}"
                
                model = CreateModel(layers=layers, layer_neurons=layer_neurons, learning_rate=learning_rate)    
                dataset = LoadDataset(path=dataset_path)

                model = FitModel(model=model, epochs=epochs, batch_size=batch_size, input_data=dataset["input_data"], output_data=dataset["output_data"])
                
                sleep(2)
                
                SendModelSuccessfulFitCompleteMessage(conn=conn)

                while True:
                    newInputData = GetDataToPredictNewAction(conn=conn)
                    new_action = PredictNewAction(model=model, input_data=newInputData)
                    
                    SendNewAction(conn=conn, actions_list=new_action)
                         
if __name__ == "__main__":
    main()