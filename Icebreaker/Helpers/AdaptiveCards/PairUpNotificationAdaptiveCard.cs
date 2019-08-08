//----------------------------------------------------------------------------------------------
// <copyright file="PairUpNotificationAdaptiveCard.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>
//----------------------------------------------------------------------------------------------

namespace Icebreaker.Helpers.AdaptiveCards
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Web.Hosting;
    using Icebreaker.Properties;

    /// <summary>
    /// Builder class for the pairup notification card
    /// </summary>
    public static class PairUpNotificationAdaptiveCard
    {
        private static readonly string CardTemplate;

        static PairUpNotificationAdaptiveCard()
        {
            var cardJsonFilePath = HostingEnvironment.MapPath("~/Helpers/AdaptiveCards/PairUpNotificationAdaptiveCard.json");
            CardTemplate = File.ReadAllText(cardJsonFilePath);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="teamName"></param>
        /// <param name="firstPersonName"></param>
        /// <param name="secondPersonName"></param>
        /// <param name="firstPersonFirstName"></param>
        /// <param name="secondPersonFirstName"></param>
        /// <param name="receiverName"></param>
        /// <param name="personUpn"></param>
        /// <param name="botDisplayName"></param>
        /// <param name="dataprovider"></param>
        /// <returns></returns>
        public static string GetCard(string teamName, string firstPersonName, string secondPersonName, string firstPersonFirstName, string secondPersonFirstName, string receiverName, string personUpn, string botDisplayName)
        {
            var title = string.Format(Resources.MeetupTitle, firstPersonFirstName, secondPersonFirstName);
            var escapedTitle = Uri.EscapeDataString(title);

            var content = string.Format(Resources.MeetupContent, botDisplayName);
            var escapedContent = Uri.EscapeDataString(content);

            var meetingLink = "https://teams.microsoft.com/l/meeting/new?subject=" + escapedTitle + "&attendees=" + personUpn + "&content=" + escapedContent;
            var matchUpCardTitleContent = Resources.MatchUpCardTitleContent;
            var matchUpCardMatchedText = string.Format(Resources.MatchUpCardMatchedText, firstPersonName);
            var matchUpCardContentPart1 = string.Format(Resources.MatchUpCardContentPart1, botDisplayName, teamName, firstPersonName);
            var matchUpCardContentPart2 = Resources.MatchUpCardContentPart2;
            var chatWithMatchButtonText = string.Format(Resources.ChatWithMatchButtonText, firstPersonFirstName);
            var pauseMatchesButtonText = Resources.PausePairingsButtonText;
            var proposeMeetupButtonText = Resources.ProposeMeetupButtonText;

            var variablesToValues = new Dictionary<string, string>()
            {
                { "matchUpCardTitleContent", matchUpCardTitleContent },
                { "matchUpCardMatchedText", matchUpCardMatchedText },
                { "matchUpCardContentPart1", matchUpCardContentPart1 },
                { "matchUpCardContentPart2", matchUpCardContentPart2 },
                { "chatWithMatchButtonText", chatWithMatchButtonText },
                { "pauseMatchesButtonText", pauseMatchesButtonText },
                { "proposeMeetupButtonText", proposeMeetupButtonText },
                { "meetingLink", meetingLink },
                { "personUpn", personUpn }
            };

            var cardBody = CardTemplate;
            foreach (var kvp in variablesToValues)
            {
                cardBody = cardBody.Replace($"%{kvp.Key}%", kvp.Value);
            }



            return cardBody;

        }
    }
}