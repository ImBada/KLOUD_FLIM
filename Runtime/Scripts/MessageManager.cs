using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KLOUD.Twitch;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.TextCore;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace KLOUD
{
    public struct MessageJob : IJobParallelForTransform
    {
        public NativeArray<bool> DeleteTargetIndexes;
        public NativeArray<float> localSpeed;
        public float Speed;
        public float TimeDelta;
        
        public void Execute(int index, TransformAccess transform)
        {
            var Pos = transform.localPosition;
            Pos.x -= Speed * TimeDelta * localSpeed[index];
            transform.localPosition = Pos;

            DeleteTargetIndexes[index] = Pos.x <= -2500;
        }
    }
    
    public class MessageManager : MonoBehaviour
    {
        public string ChennalID;
        
        public Text ReferenceText;
        public RawImage ReferenceImage;
        
        public float MasterSpeed = 10;
        public Vector2 ClampSpeedRange = new Vector2(20, 35);
        public Vector2 RandomSizeRange = new Vector2(20, 35);
        public List<Text> SpawnedTexts = new List<Text>();
        public TransformAccessArray transforms = new TransformAccessArray();
        
        public MessageJob JobCommand = new MessageJob();
        public JobHandle JobHandle;

        public WebsocketConnecter WebsocketConnecter;

        private void Start()
        {
            WebsocketConnecter = new WebsocketConnecter();
            
            WebsocketConnecter.onReceivedMessage.AddListener(
                msg =>
                {
                    var (author, ctx, emojis) = TwitchMessageParser.Parse(msg);
                    
                    if(!String.IsNullOrEmpty(author)) MessageEventManager.Enqueue(ctx, emojis);
                });

            ReferenceText.gameObject.SetActive(false);
            Refresh();
        }

        //ChennalID를 외부에서 바꾼 후 이 함수를 실행하면 채널을 전환함
        public void Refresh() {
            if (WebsocketConnecter.connectId != "") {
                WebsocketConnecter.Close();
            }
            bool isConnected = WebsocketConnecter.Connect(ChennalID);
            Debug.Log($"{ChennalID} : {isConnected}");
        }

        public void UpdateJob()
        {
            JobCommand.Speed = MasterSpeed;
            JobCommand.TimeDelta = Time.deltaTime;

            JobCommand.localSpeed = new NativeArray<float>(SpawnedTexts.Count, Allocator.Persistent);
            JobCommand.localSpeed.CopyFrom(SpawnedTexts.Select((Text t) => Mathf.Clamp(t.text.Length * 2f, ClampSpeedRange.x, ClampSpeedRange.y)).ToArray());
            
            JobCommand.DeleteTargetIndexes = new NativeArray<bool>( SpawnedTexts.Count, Allocator.Persistent );
            transforms = new TransformAccessArray(SpawnedTexts.Select((Text t) => t.transform).ToArray());
        }

        public Text SpawnText(string text, List<TwitchMessageParser.emoji> emojis)
        {
            var messageObj = Instantiate(ReferenceText.gameObject, ReferenceText.transform.parent).GetComponent<Text>();
            ((RectTransform) messageObj.transform).pivot = new Vector2(-1, 1);
            ((RectTransform) messageObj.transform).anchorMin = Vector2.one;
            ((RectTransform) messageObj.transform).anchorMax = Vector2.one;
            ((RectTransform)messageObj.transform).localPosition = new Vector3(
                messageObj.transform.localPosition.x,
                Random.Range(-400, 400),
                messageObj.transform.localPosition.z
            ); //600하면 FHD기준 가끔 삐져나가서 고침
            messageObj.fontSize = (int) Random.Range(RandomSizeRange.x, RandomSizeRange.y);
            messageObj.text = text;
            messageObj.gameObject.SetActive(true);

            foreach (var emoji in emojis)
            {
                var i = 1;

                messageObj.text = messageObj.text.Replace(emoji.tag, "<E>");
                
                while (messageObj.text.Contains("<E>"))
                {
                    if (messageObj.GetWordRectInText(out var rect, "<E>"))
                    {
                        messageObj.text = messageObj.text.ReplaceFirst("<E>", "   ");
                        var emojiObj = Instantiate(ReferenceImage.gameObject, messageObj.transform).GetComponent<RawImage>();
                        var emojiRenderer = emojiObj.gameObject.AddComponent<EmojiRenderer>();

                        emojiRenderer.Emoji = emoji;
                        emojiRenderer.Image = emojiObj;
                        emojiRenderer.Load();

                        emojiObj.rectTransform.anchoredPosition = new Vector2(rect.x, 0);
                        emojiObj.rectTransform.sizeDelta = new Vector2(
                            messageObj.rectTransform.sizeDelta.y,
                            messageObj.rectTransform.sizeDelta.y);

                        emojiObj.rectTransform.parent = messageObj.rectTransform;
                        i++;
                    }
                    else
                    {
                        break;
                    }
                }
                
            }
            

            return messageObj;
        }

        private void Update()
        {
            if (JobHandle.IsCompleted)
            {
                JobHandle.Complete();
                var i = 0;
                foreach (var b in JobCommand.DeleteTargetIndexes)
                {
                    if (!b)
                    {
                        i++;
                        continue;
                    }
                    var target = SpawnedTexts[i];
                    SpawnedTexts.RemoveAt(i);
                    Destroy(target.gameObject);
                }
                
                if(transforms.isCreated) transforms.Dispose();
                if (JobCommand.localSpeed.IsCreated) JobCommand.localSpeed.Dispose();
                if(JobCommand.DeleteTargetIndexes.IsCreated) JobCommand.DeleteTargetIndexes.Dispose();

                while (!MessageEventManager.IsEmpty())
                {
                    var messageEvent = MessageEventManager.Dequeue();
                    var spawnedText = SpawnText(messageEvent.Message, messageEvent.Emojis);
                    SpawnedTexts.Add(spawnedText);
                }
                UpdateJob();

                JobHandle = JobCommand.Schedule(transforms);
            }
        }
    }
}
