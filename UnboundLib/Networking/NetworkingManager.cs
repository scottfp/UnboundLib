﻿using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnboundLib.Networking;

namespace UnboundLib
{
    public static class NetworkingManager
    {
        //private static bool initialized = false;

        private static RaiseEventOptions raiseEventOptionsAll = new RaiseEventOptions
        {
            Receivers = ReceiverGroup.All
        };
        private static RaiseEventOptions raiseEventOptionsOthers = new RaiseEventOptions
        {
            Receivers = ReceiverGroup.Others
        };
        private static SendOptions sendOptions = new SendOptions
        {
            Reliability = true
        };

        public delegate void PhotonEvent(object[] objects);
        private static Dictionary<string, PhotonEvent> events = new Dictionary<string, PhotonEvent>();
        private static Dictionary<Tuple<Type, string>, MethodInfo> rpcMethodCache = new Dictionary<Tuple<Type, string>, MethodInfo>();

        private static byte ModEventCode = 69;

        static NetworkingManager()
        {
            PhotonNetwork.NetworkingClient.EventReceived += OnEvent;
        }

        public static void RegisterEvent(string eventName, PhotonEvent handler)
        {
            if (events.ContainsKey(eventName))
            {
                throw new Exception($"An event handler is already registered with the event name: {eventName}");
            }

            events.Add(eventName, handler);
        }
        public static void RaiseEvent(string eventName, RaiseEventOptions options, params object[] data) {
            if (data == null) data = new object[0];
            var allData = new List<object>();
            allData.Add(eventName);
            allData.AddRange(data);
            PhotonNetwork.RaiseEvent(ModEventCode, allData.ToArray(), options, sendOptions);
        }
        public static void RaiseEvent(string eventName, params object[] data)
        {
            RaiseEvent(eventName, raiseEventOptionsAll, data);
        }
        public static void RaiseEventOthers(string eventName, params object[] data)
        {
            RaiseEvent(eventName, raiseEventOptionsOthers, data);
        }
        public static void RPC(Type targetType, string methodName, RaiseEventOptions options, params object[] data)
        {
            if (data == null) data = new object[0];

            if (PhotonNetwork.OfflineMode || PhotonNetwork.CurrentRoom == null) {
                var methodInfo = GetRPCMethod(targetType, methodName);
                if (methodInfo != null)
                {
                    methodInfo.Invoke(null, data.ToArray());
                }
                return;
            }

            var allData = new List<object>();
            allData.Add(targetType.AssemblyQualifiedName);
            allData.Add(methodName);
            allData.AddRange(data);
            PhotonNetwork.RaiseEvent(ModEventCode, allData.ToArray(), options, sendOptions);
        }
        public static void RPC(Type targetType, string methodName, params object[] data)
        {
            RPC(targetType, methodName, raiseEventOptionsAll, data);
        }
        public static void RPC_Others(Type targetType, string methodName, params object[] data)
        {
            RPC(targetType, methodName, raiseEventOptionsOthers, data);
        }

        public static void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code != ModEventCode) return;

            object[] data = null;

            try
            {
                data = (object[])photonEvent.CustomData;
            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
            }

            try
            {
                var type = Type.GetType((string)data[0]);
                if (type != null)
                {
                    var methodInfo = GetRPCMethod(type, (string) data[1]);
                    if (methodInfo != null)
                    {
                        methodInfo.Invoke(null, data.Skip(2).ToArray());
                    }
                }
                else if (events.TryGetValue((string)data[0], out PhotonEvent handler))
                {
                    handler?.Invoke(data.Skip(1).ToArray());
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Network Error: \n" + e.ToString());
            }
        }

        private static MethodInfo GetRPCMethod(Type type, string methodName) {
            var key = new Tuple<Type, string>(type, methodName);

            if (!rpcMethodCache.ContainsKey(key))
            {
                var methodInfo = (from m in type.GetMethods()
                                  let attr = m.GetCustomAttribute<UnboundRPC>()
                                  where attr != null
                                  let name = attr.EventID == null ? m.Name : attr.EventID
                                  where methodName == name
                                  select m).FirstOrDefault();
                rpcMethodCache.Add(key, methodInfo);
            }

            return rpcMethodCache[key];
        }
    }
}
