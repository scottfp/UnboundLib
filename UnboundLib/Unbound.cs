﻿using BepInEx;
using HarmonyLib;
using Photon.Pun;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnboundLib.GameModes;
using UnboundLib.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace UnboundLib
{
    [BepInPlugin(ModId, ModName, Version)]
    [BepInProcess("Rounds.exe")]
    public class Unbound : BaseUnityPlugin
    {
        private const string ModId = "com.willis.rounds.unbound";
        private const string ModName = "Rounds Unbound";
        public const string Version = "2.1.2";

        public static Unbound Instance { get; private set; }

        private Canvas _canvas;
        public Canvas canvas
        {
            get
            {
                if (_canvas == null)
                {
                    _canvas = new GameObject("UnboundLib Canvas").AddComponent<Canvas>();
                    _canvas.gameObject.AddComponent<GraphicRaycaster>();
                    _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    _canvas.pixelPerfect = false;
                    DontDestroyOnLoad(_canvas);
                }
                return _canvas;
            }
        }

        struct NetworkEventType
        {
            public const string
                StartHandshake = "ModLoader_HandshakeStart",
                FinishHandshake = "ModLoader_HandshakeFinish";
        }

        internal static CardInfo templateCard;
        internal static CardInfo[] defaultCards;
        internal static List<CardInfo> activeCards = new List<CardInfo>();
        internal static List<CardInfo> inactiveCards = new List<CardInfo>();
        internal static List<string> levels = new List<string>();

        public delegate void OnJoinedDelegate();
        public delegate void OnLeftDelegate();
        public static event OnJoinedDelegate OnJoinedRoom;
        public static event OnLeftDelegate OnLeftRoom;

        internal static Dictionary<string, GUIListener> GUIListeners = new Dictionary<string, GUIListener>();
        internal static List<Action> handShakeActions = new List<Action>();

        private static bool showModUi = false;

        internal static AssetBundle UIAssets;
        private static GameObject modalPrefab;

        public Unbound()
        {
            // Add UNBOUND text to the main menu screen
            TextMeshProUGUI text = null;
            bool firstTime = true;
            bool canCreate = true;

            On.MainMenuHandler.Awake += (orig, self) =>
            {
                canCreate = true;
                this.ExecuteAfterSeconds(firstTime ? 4f : 0.1f, () =>
                {
                    if (!canCreate) return;
                    var pos = new Vector2(Screen.width / 2, Screen.height * 0.75f + 25);
                    text = CreateTextAt("UNBOUND", Vector2.zero);
                    text.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    text.fontSize = 30;
                    text.color = (Color.yellow + Color.red) / 2;
                    text.font = ((TextMeshProUGUI) FindObjectOfType<ListMenuButton>().GetFieldValue("text")).font;
                    text.transform.SetParent(MainMenuHandler.instance.transform.Find("Canvas/ListSelector/Main/Group"), true);
                    text.transform.SetAsFirstSibling();
                    text.rectTransform.localScale = Vector3.one;
                    text.rectTransform.localPosition = new Vector3(0, 325, text.rectTransform.localPosition.z);
                    // Add custom levels to map list
                    MapManager.instance.levels = levels.ToArray();
                });
                firstTime = false;

                orig(self);
            };

            On.MainMenuHandler.Close += (orig, self) =>
            {
                canCreate = false;
                if (text != null) Destroy(text.gameObject);

                orig(self);
            };

            IEnumerator ArmsRaceStartCoroutine(On.GM_ArmsRace.orig_Start orig, GM_ArmsRace self)
            {
                yield return GameModeManager.TriggerHook(GameModeHooks.HookInitStart);
                orig(self);
                yield return GameModeManager.TriggerHook(GameModeHooks.HookInitEnd);
            }

            On.GM_ArmsRace.Start += (orig, self) =>
            {
                self.StartCoroutine(ArmsRaceStartCoroutine(orig, self));
            };
        }

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                DestroyImmediate(gameObject);
                return;
            }

            // Patch game with Harmony
            var harmony = new Harmony(ModId);
            harmony.PatchAll();

            LoadAssets();
            GameModeManager.Init();
        }

        void Start()
        {
            // store default cards
            defaultCards = (CardInfo[]) CardChoice.instance.cards.Clone();

            // request mod handshake
            NetworkingManager.RegisterEvent(NetworkEventType.StartHandshake, (data) =>
            {
                if (PhotonNetwork.IsMasterClient)
                {
                    NetworkingManager.RaiseEvent(NetworkEventType.FinishHandshake,
                                                 GameModeManager.CurrentHandlerID,
                                                 GameModeManager.CurrentHandler?.Settings);
                }
                else
                {
                    NetworkingManager.RaiseEvent(NetworkEventType.FinishHandshake);
                }

                CardChoice.instance.cards = defaultCards;
            });

            // receive mod handshake
            NetworkingManager.RegisterEvent(NetworkEventType.FinishHandshake, (data) =>
            {
                CardChoice.instance.cards = activeCards.ToArray();

                if (data.Length > 0)
                {
                    GameModeManager.SetGameMode((string) data[0], false);
                    GameModeManager.CurrentHandler.SetSettings((GameSettings) data[1]);
                }
            });

            // fetch card to use as a template for all custom cards
            templateCard = (from c in CardChoice.instance.cards
                            where c.cardName.ToLower() == "huge"
                            select c).FirstOrDefault();
            defaultCards = CardChoice.instance.cards;
            activeCards.AddRange(defaultCards);

            // register default cards with toggle menu
            foreach (var card in defaultCards)
            {
                CardToggleMenuHandler.Instance.AddCardToggle(card, false);
            }
            
            // add default levels to level list
            levels.AddRange(MapManager.instance.levels);

            // hook up Photon callbacks
            var networkEvents = gameObject.AddComponent<NetworkEventCallbacks>();
            networkEvents.OnJoinedRoomEvent += OnJoinedRoomAction;
            networkEvents.OnLeftRoomEvent += OnLeftRoomAction;
        }

        void Update()
        {
            if (GameManager.instance.isPlaying && PhotonNetwork.OfflineMode)
            {
                CardChoice.instance.cards = activeCards.ToArray();
            }

            if (Input.GetKeyDown(KeyCode.F1))
            {
                showModUi = !showModUi;
            }

            GameManager.lockInput = showModUi || DevConsole.isTyping;
        }

        void OnGUI()
        {
            if (!showModUi) return;

            Vector2 center = new Vector2(Screen.width / 2, Screen.height / 2);
            Vector2 size = new Vector2(400, 100 * GUIListeners.Count);

            GUILayout.BeginVertical();

            bool showingSpecificMod = false;
            foreach (var md in GUIListeners.Keys)
            {
                var data = GUIListeners[md];
                if (data.guiEnabled)
                {
                    if (GUILayout.Button("<- Back"))
                    {
                        data.guiEnabled = false;
                    }
                    GUILayout.Label(data.modName + " Options");
                    showingSpecificMod = true;
                    data.guiAction?.Invoke();
                    break;
                }
            }

            if (showingSpecificMod) return;

            GUILayout.Label("UnboundLib Options");
            if (GUILayout.Button("Toggle Cards"))
            {
                CardToggleMenuHandler.Instance.Show();
            }

            GUILayout.Label("Mod Options:");
            foreach (var md in GUIListeners.Keys)
            {
                var data = GUIListeners[md];
                if (GUILayout.Button(data.modName))
                {
                    data.guiEnabled = true;
                }
            }
            GUILayout.EndVertical();
        }

        void LoadAssets()
        {
            UIAssets = Jotunn.Utils.AssetUtils.LoadAssetBundleFromResources("unboundui", typeof(Unbound).Assembly);
            if (UIAssets != null)
            {
                modalPrefab = UIAssets.LoadAsset<GameObject>("Modal");
                Instantiate(UIAssets.LoadAsset<GameObject>("Card Toggle Menu"), canvas.transform).AddComponent<CardToggleMenuHandler>();
            }
        }

        private void OnJoinedRoomAction()
        {
            NetworkingManager.RaiseEventOthers(NetworkEventType.StartHandshake);

            OnJoinedRoom?.Invoke();
            foreach (var handshake in handShakeActions)
            {
                handshake?.Invoke();
            }
        }
        private void OnLeftRoomAction()
        {
            CardChoice.instance.cards = defaultCards;
            OnLeftRoom?.Invoke();
        }

        [UnboundRPC]
        public static void BuildInfoPopup(string message)
        {
            var popup = new GameObject("Info Popup").AddComponent<InfoPopup>();
            popup.rectTransform.SetParent(Instance.canvas.transform);
            popup.Build(message);
        }
        public static ModalHandler BuildModal()
        {
            return Instantiate(modalPrefab, Instance.canvas.transform).AddComponent<ModalHandler>();
        }

        public static void RegisterGUI(string modName, Action guiAction)
        {
            GUIListeners.Add(modName, new GUIListener(modName, guiAction));
        }
        public static void RegisterHandshake(string modId, Action callback)
        {
            // register mod handshake network events
            NetworkingManager.RegisterEvent($"ModLoader_{modId}_StartHandshake", (e) =>
            {
                NetworkingManager.RaiseEvent($"ModLoader_{modId}_FinishHandshake");
            });
            NetworkingManager.RegisterEvent($"ModLoader_{modId}_FinishHandshake", (e) =>
            {
                callback?.Invoke();
            });
            handShakeActions.Add(() => NetworkingManager.RaiseEventOthers($"ModLoader_{modId}_StartHandshake"));
        }

        public static TextMeshProUGUI CreateTextAt(string text, Vector2 position)
        {
            var newText = new GameObject("Unbound Text Object").AddComponent<TextMeshProUGUI>();
            newText.text = text;
            newText.fontSize = 100;
            newText.transform.SetParent(Instance.canvas.transform);

            var anchorPoint = new Vector2(0.5f, 0.5f);
            newText.rectTransform.anchorMax = anchorPoint;
            newText.rectTransform.anchorMin = anchorPoint;
            newText.rectTransform.pivot = anchorPoint;
            newText.overflowMode = TextOverflowModes.Overflow;
            newText.alignment = TextAlignmentOptions.Center;
            newText.rectTransform.position = position;
            newText.enableWordWrapping = false;

            Instance.StartCoroutine(FadeIn(newText.gameObject.AddComponent<CanvasGroup>(), 4));

            return newText;
        }

        public static void BuildLevel(AssetBundle assetBundle)
        {
            foreach (var path in assetBundle.GetAllScenePaths())
            {
                levels.Add(path);
            }
        }
        // loads a map in via its name and it start with /
        internal static void SpawnMap(string message)
        {
            if (!message.StartsWith("/"))
            {
                return;
            }
            // search code copied from card search 
            try
            {
                var currentLevels = MapManager.instance.levels;
                var num = -1;
                var num2 = 0f;
                for (var i = 0; i < currentLevels.Length; i++)
                {
                    var text = currentLevels[i].ToUpper();
                    text = text.Replace(" ", "");
                    text = text.Replace("ASSETS", "");
                    text = text.Replace(".UNITY", "");
                    text = Regex.Replace(text, "/.*/", string.Empty);
                    text = text.Replace("/", "");
                    var text2 = message.ToUpper();
                    text2 = text2.Replace(" ", "_");
                    text2 = text2.Replace("/", "");
                    var num3 = 0f;
                    for (int j = 0; j < text2.Length; j++)
                    {
                        if (text.Length > j && text2[j] == text[j])
                        {
                            num3 += 1f / text2.Length;
                        }
                    }
                    num3 -= (float)Mathf.Abs(text2.Length - text.Length) * 0.001f;
                    if (num3 > 0.1f && num3 > num2)
                    {
                        num2 = num3;
                        num = i;
                    }
                }
                if (num != -1)
                {
                    MapManager.instance.LoadLevelFromID(num, false, true);
                    
                    foreach (var player in PlayerManager.instance.players)
                    {
                        player.data.healthHandler.Revive();
                    }
                }
            }
            catch
            {
            }
        }

        private static IEnumerator FadeIn(CanvasGroup target, float seconds)
        {
            float startTime = Time.time;
            target.alpha = 0;
            while (Time.time - startTime < seconds)
            {
                target.alpha = (Time.time - startTime) / seconds;
                yield return null;
            }
            target.alpha = 1;
        }

        internal class GUIListener
        {
            public bool guiEnabled = false;
            public string modName;
            public Action guiAction;
            public GUIListener(string modName, Action guiAction)
            {
                this.modName = modName;
                this.guiAction = guiAction;
            }
        }
    }
}
