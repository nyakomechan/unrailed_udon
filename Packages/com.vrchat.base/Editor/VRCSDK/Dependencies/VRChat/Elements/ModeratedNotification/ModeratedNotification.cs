using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDKBase.Editor.Api;
using VRC.SDKBase.Editor.Elements;

namespace VRC.SDK3.Editor.Elements
{
    public class ModeratedNotification: VisualElement
    {
        private const string ModerationTitle = "Content Guidelines Check";
        private const string ModerationBody = "Please update your {0} to remove or revise wording that does not meet our content guidelines.";
        private const string LearnMore = "Learn More";
        private const string ModerationLearnMoreBody = "A word or phrase in your {0} does not align with our content guidelines and must be adjusted. Please check out our content guidelines below.\nWas this detection wrong? Let us know!";
        private const string ModerationDisagree = "This detection was wrong";
        private const string ModerationViewGuidelines = "View Content Guidelines";
        private const string Okay = "Okay";
        private const string ModerationDisagreeSuccess = "Thank you for letting us know! We will look into what happened.";

        private const string ContentGuidelinesUrl = "https://vrch.at/text-guidelines";

        private readonly VRCSdkControlPanel _builder;

        public ModeratedNotification(VRCSdkControlPanel builder, string[] fields)
        {
            _builder = builder;

            Resources.Load<VisualTreeAsset>("ModeratedNotification").CloneTree(this);
            styleSheets.Add(Resources.Load<StyleSheet>("ModeratedNotificationStyles"));

            var body = BuildBody(fields);
            var learnMoreBody = BuildLearnMoreBody(fields);

            this.Q<Label>("main-text").text = body;
            this.Q<Label>("details-text").text = learnMoreBody;

            var learnMoreButton = this.Q<Button>("learn-more-button");
            learnMoreButton.text = LearnMore;
            learnMoreButton.clicked += OpenContentGuidelines;

            var disagreeButton = this.Q<Button>("disagree-button");
            disagreeButton.text = ModerationDisagree;
            disagreeButton.clicked += ShowDisagreeModal;
        }

        private static string BuildBody(string[] fields)
        {
            var joinedFields = JoinFields(fields);
            return string.Format(ModerationBody, joinedFields);
        }

        private static string BuildLearnMoreBody(string[] fields)
        {
            var joinedFields = JoinFields(fields);
            return string.Format(ModerationLearnMoreBody, joinedFields);
        }

        private static string JoinFields(string[] fields)
        {
            if (fields == null || fields.Length == 0)
                return "content";

            return string.Join(", ", fields.Select(LocalizeField).ToArray());
        }

        private static string LocalizeField(string field)
        {
            return field switch
            {
                "bio" => "Bio",
                "statusDescription" => "Status",
                "pronouns" => "Pronouns",
                "displayName" => "Name",
                "image" => "Image",
                "name" => "Name",
                "description" => "Description",
                _ => field
            };
        }

        private async void ShowDisagreeModal()
        {
            try
            {
                await VRCApi.ContestModeration();
            }
            catch (RequestFailedException e)
            {
                Debug.LogError("Failed to contest moderation");
                Debug.LogException(e);
                Modal.CreateAndShow(
                    "Error",
                    $"{(int)e.HttpMessage.StatusCode} {e.HttpMessage.ReasonPhrase}",
                    null,
                    Okay,
                    this
                );
                return;
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to contest moderation");
                Debug.LogException(e);
                Modal.CreateAndShow(
                    "Error",
                    e.Message,
                    null,
                    Okay,
                    this
                );
                return;
            }

            Modal.CreateAndShow(
                ModerationTitle,
                ModerationDisagreeSuccess,
                async () => await _builder.DismissNotification(),
                Okay,
                this
            );
        }

        private static void OpenContentGuidelines()
        {
            Application.OpenURL(ContentGuidelinesUrl);
        }
    }
}