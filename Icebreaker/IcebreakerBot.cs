//----------------------------------------------------------------------------------------------
// <copyright file="IcebreakerBot.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>
//----------------------------------------------------------------------------------------------

namespace Icebreaker
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Helpers;
    using Helpers.AdaptiveCards;
    using Icebreaker.Properties;
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.Azure;
    using Microsoft.Bot.Connector;
    using Microsoft.Bot.Connector.Teams;
    using Microsoft.Bot.Connector.Teams.Models;
    using Newtonsoft.Json;

    /// <summary>
    /// Implements the core logic for Icebreaker bot
    /// </summary>
    public class IcebreakerBot
    {
        private readonly IcebreakerBotDataProvider dataProvider;
        private readonly TelemetryClient telemetryClient;
        private readonly int maxPairUpsPerTeam;
        private readonly string botDisplayName;
        private readonly string botId;
        private readonly bool isTesting;

        /// <summary>
        /// Initializes a new instance of the <see cref="IcebreakerBot"/> class.
        /// </summary>
        /// <param name="dataProvider">The data provider to use</param>
        /// <param name="telemetryClient">The telemetry client to use</param>
        public IcebreakerBot(IcebreakerBotDataProvider dataProvider, TelemetryClient telemetryClient)
        {
            this.dataProvider = dataProvider;
            this.telemetryClient = telemetryClient;
            this.maxPairUpsPerTeam = Convert.ToInt32(CloudConfigurationManager.GetSetting("MaxPairUpsPerTeam"));
            this.botDisplayName = CloudConfigurationManager.GetSetting("BotDisplayName");
            this.botId = CloudConfigurationManager.GetSetting("MicrosoftAppId");
            this.isTesting = Convert.ToBoolean(CloudConfigurationManager.GetSetting("Testing"));
        }

        /// <summary>
        /// Generate pairups and send pairup notifications.
        /// </summary>
        /// <returns>The number of pairups that were made</returns>
        public async Task<int> MakePairsAndNotify()
        {
            this.telemetryClient.TrackTrace("Making pairups");

            // Recall all the teams where we have been added
            // For each team where bot has been added:
            //     Pull the roster of the team
            //     Remove the members who have opted out of pairups
            //     Match each member with someone else
            //     Save this pair
            // Now notify each pair found in 1:1 and ask them to reach out to the other person
            // When contacting the user in 1:1, give them the button to opt-out
            var installedTeamsCount = 0;
            var pairsNotifiedCount = 0;
            var usersNotifiedCount = 0;

            try
            {
                var teams = await this.dataProvider.GetInstalledTeamsAsync();
                installedTeamsCount = teams.Count;
                this.telemetryClient.TrackTrace($"Generating pairs for {installedTeamsCount} teams");

                foreach (var team in teams)
                {
                    this.telemetryClient.TrackTrace($"Pairing members of team {team.Id}");

                    try
                    {
                        MicrosoftAppCredentials.TrustServiceUrl(team.ServiceUrl);
                        var connectorClient = new ConnectorClient(new Uri(team.ServiceUrl));

                        var teamName = await this.GetTeamNameAsync(connectorClient, team.TeamId);
                        var optedInUsers = await this.GetOptedInUsers(connectorClient, team);

                        foreach (var pair in this.MakePairs(optedInUsers).Take(this.maxPairUpsPerTeam))
                        {
                            usersNotifiedCount += await this.NotifyPair(connectorClient, team.TenantId, teamName, pair, team.TeamId);

                            usersNotifiedCount += await this.NotifyPair(connectorClient, team.TenantId, teamName, pair);

                            pairsNotifiedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        this.telemetryClient.TrackTrace($"Error pairing up team members: {ex.Message}", SeverityLevel.Warning);
                        this.telemetryClient.TrackException(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                this.telemetryClient.TrackTrace($"Error making pairups: {ex.Message}", SeverityLevel.Warning);
                this.telemetryClient.TrackException(ex);
            }

            // Log telemetry about the pairups
            var properties = new Dictionary<string, string>
            {
                { "InstalledTeamsCount", installedTeamsCount.ToString() },
                { "PairsNotifiedCount", pairsNotifiedCount.ToString() },
                { "UsersNotifiedCount", usersNotifiedCount.ToString() },
            };
            this.telemetryClient.TrackEvent("ProcessedPairups", properties);

            this.telemetryClient.TrackTrace($"Made {pairsNotifiedCount} pairups, {usersNotifiedCount} notifications sent");
            return pairsNotifiedCount;
        }

        /// <summary>
        /// Generate pairups and send pairup notifications.
        /// </summary>
        /// <returns>The number of pairups that were made</returns>
        public async Task<int> MakeFeedbackNotify()
        {
            this.telemetryClient.TrackTrace("Making feedback");

            // Recall all the teams where we have been added
            // For each team where bot has been added:
            //     Pull the roster of the team
            //     Remove the members who have opted out of pairups
            //     Match each member with someone else
            //     Save this pair
            // Now notify each pair found in 1:1 and ask them to reach out to the other person
            // When contacting the user in 1:1, give them the button to opt-out
            var installedTeamsCount = 0;
            var pairsNotifiedCount = 0;
            var usersNotifiedCount = 0;

            try
            {
                var pairupUsers = await this.dataProvider.GetPairUpUsersAsync();
                installedTeamsCount = pairupUsers.Count;
                this.telemetryClient.TrackTrace($"Getting feedback for {installedTeamsCount} teams");

                foreach (var pairupUser in pairupUsers)
                {
                    this.telemetryClient.TrackTrace($"Pairing members of team {pairupUser.Id}");

                    try
                    {
                        MicrosoftAppCredentials.TrustServiceUrl(pairupUser.ServiceURL);
                        var connectorClient = new ConnectorClient(new Uri(pairupUser.ServiceURL));

                        var teamDetails = await this.GetTeamAsync(connectorClient, pairupUser.TeamId);
                        var team = await this.dataProvider.GetInstalledTeamAsync(pairupUser.TeamId);
                        var optedInUsers = await this.GetOptedInUsers(connectorClient, team);
                        List<ChannelAccount> channelAccounts = new List<ChannelAccount>();
                        TimeSpan diff = DateTime.Now - pairupUser.ScheduledDate;
                        double hours = diff.TotalHours;

                        if (hours > 1)
                        {
                            foreach (var optedInUser in optedInUsers)
                            {
                                if (pairupUser.FirstPersonFirstName == optedInUser.Name || pairupUser.SecondPersonFirstName == optedInUser.Name)
                                {
                                    channelAccounts.Add(optedInUser);
                                    pairsNotifiedCount++;
                                }
                            }

                            if (channelAccounts.Count > 0)
                            {
                                usersNotifiedCount += await this.FeedbackUser(connectorClient, team.TenantId, string.Empty, channelAccounts);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        this.telemetryClient.TrackTrace($"Error pairing up team members: {ex.Message}", SeverityLevel.Warning);
                        this.telemetryClient.TrackException(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                this.telemetryClient.TrackTrace($"Error making pairups: {ex.Message}", SeverityLevel.Warning);
                this.telemetryClient.TrackException(ex);
            }

            // Log telemetry about the pairups
            var properties = new Dictionary<string, string>
            {
                { "InstalledTeamsCount", installedTeamsCount.ToString() },
                { "PairsNotifiedCount", pairsNotifiedCount.ToString() },
                { "UsersNotifiedCount", usersNotifiedCount.ToString() },
            };
            this.telemetryClient.TrackEvent("ProcessedPairups", properties);

            this.telemetryClient.TrackTrace($"Made {pairsNotifiedCount} pairups, {usersNotifiedCount} notifications sent");
            return pairsNotifiedCount;
        }

        /// <summary>
        /// Method that will return the information of the installed team
        /// </summary>
        /// <param name="teamId">The team id</param>
        /// <returns>The team that the bot has been installed to</returns>
        public Task<TeamInstallInfo> GetInstalledTeam(string teamId)
        {
            return this.dataProvider.GetInstalledTeamAsync(teamId);
        }

        /// <summary>
        /// Send a welcome message to the user that was just added to a team.
        /// </summary>
        /// <param name="connectorClient">The connector client</param>
        /// <param name="memberAddedId">The id of the added user</param>
        /// <param name="tenantId">The tenant id</param>
        /// <param name="teamId">The id of the team the user was added to</param>
        /// <param name="botInstaller">The person that installed the bot</param>
        /// <returns>Tracking task</returns>
        public async Task WelcomeUser(ConnectorClient connectorClient, string memberAddedId, string tenantId, string teamId, string botInstaller)
        {
            this.telemetryClient.TrackTrace($"Sending welcome message for user {memberAddedId}");

            var teamName = await this.GetTeamNameAsync(connectorClient, teamId);
            var allMembers = await connectorClient.Conversations.GetConversationMembersAsync(teamId);

            ChannelAccount userThatJustJoined = null;
            foreach (var m in allMembers)
            {
                // both values are 29: values
                if (m.Id == memberAddedId)
                {
                    userThatJustJoined = m;
                    break;
                }
            }

            if (userThatJustJoined != null)
            {
                var welcomeMessageCard = WelcomeNewMemberAdaptiveCard.GetCard(teamName, userThatJustJoined.Name, this.botDisplayName, botInstaller);
                await this.NotifyUser(connectorClient, welcomeMessageCard, userThatJustJoined, tenantId);
            }
            else
            {
                this.telemetryClient.TrackTrace($"Member {memberAddedId} was not found in team {teamId}, skipping welcome message.", SeverityLevel.Warning);
            }
        }

        /// <summary>
        /// Sends a welcome message to the General channel of the team that this bot has been installed to
        /// </summary>
        /// <param name="connectorClient">The connector client</param>
        /// <param name="teamId">The id of the team that the bot is installed to</param>
        /// <param name="botInstaller">The installer of the application</param>
        /// <returns>Tracking task</returns>
        public async Task WelcomeTeam(ConnectorClient connectorClient, string teamId, string botInstaller)
        {
            this.telemetryClient.TrackTrace($"Sending welcome message for team {teamId}");

            var teamName = await this.GetTeamNameAsync(connectorClient, teamId);
            var welcomeTeamMessageCard = WelcomeTeamAdaptiveCard.GetCard(teamName, this.botDisplayName, botInstaller);
            await this.NotifyTeam(connectorClient, welcomeTeamMessageCard, teamId);
        }

        /// <summary>
        /// Sends a message whenever there is unrecognized input into the bot
        /// </summary>
        /// <param name="connectorClient">The connector client</param>
        /// <param name="replyActivity">The activity for replying to a message</param>
        /// <returns>Tracking task</returns>
        public async Task SendUnrecognizedInputMessage(ConnectorClient connectorClient, Activity replyActivity)
        {
            var unrecognizedInputAdaptiveCard = UnrecognizedInputAdaptiveCard.GetCard();
            replyActivity.Attachments = new List<Attachment>()
            {
                new Attachment()
                {
                    ContentType = "application/vnd.microsoft.card.adaptive",
                    Content = JsonConvert.DeserializeObject(unrecognizedInputAdaptiveCard)
                }
            };
            await connectorClient.Conversations.ReplyToActivityAsync(replyActivity);
        }

        /// <summary>
        /// Save information about the team to which the bot was added.
        /// </summary>
        /// <param name="serviceUrl">The service url</param>
        /// <param name="teamId">The team id</param>
        /// <param name="tenantId">The tenant id</param>
        /// <param name="botInstaller">Person that has added the bot to the team</param>
        /// <returns>Tracking task</returns>
        public Task SaveAddedToTeam(string serviceUrl, string teamId, string tenantId, string botInstaller)
        {
            var teamInstallInfo = new TeamInstallInfo
            {
                ServiceUrl = serviceUrl,
                TeamId = teamId,
                TenantId = tenantId,
                InstallerName = botInstaller
            };
            return this.dataProvider.UpdateTeamInstallStatusAsync(teamInstallInfo, true);
        }
        /// Save information about the pair up users details
        /// </summary>
        /// <param name="escapedTitle">The escaped title</param>
        /// <param name="scheduledDate">Pair up time</param>
        /// <param name="personUpn">UPN name</param>
        /// <param name="firstPersonFirstName">The first person fisrt name</param>
        /// <param name="secondPersonFirstName">The second person first name</param>
        /// <param name="isPaired">to identify whether card is generated or not</param>
        /// <param name="teamId"></param>
        /// <param name="serviceURL"></param>
        /// <returns>Tracking task</returns>
        public Task SavePairUpusers(string escapedTitle, DateTime scheduledDate, string personUpn, string firstPersonFirstName, string secondPersonFirstName, bool isPaired, string teamId, string serviceURL)
        {
            var pairupUsersInfo = new PairupUsers
            {
                escapedTitle = escapedTitle,
                ScheduledDate = scheduledDate,
                PersonUpn = personUpn,
                FirstPersonFirstName = firstPersonFirstName,
                SecondPersonFirstName = secondPersonFirstName,
                Ispaired = isPaired,
                TeamId = teamId,
                ServiceURL = serviceURL
            };
            return this.dataProvider.UpdatePairupUsersAsync(pairupUsersInfo, true);
        }
        /// <summary>
        /// Save information about the team from which the bot was removed.
        /// </summary>
        /// <param name="serviceUrl">The service url</param>
        /// <param name="teamId">The team id</param>
        /// <param name="tenantId">The tenant id</param>
        /// <returns>Tracking task</returns>
        public Task SaveRemoveFromTeam(string serviceUrl, string teamId, string tenantId)
        {
            var teamInstallInfo = new TeamInstallInfo
            {
                TeamId = teamId,
                TenantId = tenantId,
            };
            return this.dataProvider.UpdateTeamInstallStatusAsync(teamInstallInfo, false);
        }

        /// <summary>
        /// Opt out the user from further pairups
        /// </summary>
        /// <param name="tenantId">The tenant id</param>
        /// <param name="userId">The user id</param>
        /// <param name="serviceUrl">The service url</param>
        /// <returns>Tracking task</returns>
        public Task OptOutUser(string tenantId, string userId, string serviceUrl)
        {
            return this.dataProvider.SetUserInfoAsync(tenantId, userId, false, serviceUrl);
        }

        /// <summary>
        /// Opt in the user to pairups
        /// </summary>
        /// <param name="tenantId">The tenant id</param>
        /// <param name="userId">The user id</param>
        /// <param name="serviceUrl">The service url</param>
        /// <returns>Tracking task</returns>
        public Task OptInUser(string tenantId, string userId, string serviceUrl)
        {
            return this.dataProvider.SetUserInfoAsync(tenantId, userId, true, serviceUrl);
        }

        /// Save information about the team to which the bot was added.
        /// </summary>
        /// <param name="feedbackInfo">The meeting rate</param>
        /// <returns>Tracking task</returns>
        public Task SaveFeedbackInfo(FeedbackInfo feedbackInfo)
        {
            return this.dataProvider.UpdateFeedbackInfoAsync(feedbackInfo, true);
        }

        /// <summary>
        /// Get the name of a team.
        /// </summary>
        /// <param name="connectorClient">The connector client</param>
        /// <param name="teamId">The team id</param>
        /// <returns>The name of the team</returns>
        private async Task<string> GetTeamNameAsync(ConnectorClient connectorClient, string teamId)
        {
            var teamsConnectorClient = connectorClient.GetTeamsConnectorClient();
            var teamDetailsResult = await teamsConnectorClient.Teams.FetchTeamDetailsAsync(teamId);
            return teamDetailsResult.Name;
        }

        /// <summary>

        /// Get the team.
        /// </summary>
        /// <param name="connectorClient">The connector client</param>
        /// <param name="teamId">The team id</param>
        /// <returns>The name of the team</returns>
        private async Task<TeamDetails> GetTeamAsync(ConnectorClient connectorClient, string teamId)
        {
            var teamsConnectorClient = connectorClient.GetTeamsConnectorClient();
            var teamDetailsResult = await teamsConnectorClient.Teams.FetchTeamDetailsAsync(teamId);
            return teamDetailsResult;
        }

        /// <summary>
        /// Notify a pairup.
        /// </summary>
        /// <param name="connectorClient">The connector client</param>
        /// <param name="tenantId">The tenant id</param>
        /// <param name="teamName">The team name</param>
        /// <param name="pair">The pairup</param>
        /// <param name="teamId"> The team id</param>
        /// <returns>Number of users notified successfully</returns>
        private async Task<int> NotifyPair(ConnectorClient connectorClient, string tenantId, string teamName, Tuple<ChannelAccount, ChannelAccount> pair, string teamId)

        /// <returns>Number of users notified successfully</returns>
        private async Task<int> NotifyPair(ConnectorClient connectorClient, string tenantId, string teamName, Tuple<ChannelAccount, ChannelAccount> pair)
        {
            this.telemetryClient.TrackTrace($"Sending pairup notification to {pair.Item1.Id} and {pair.Item2.Id}");

            var teamsPerson1 = pair.Item1.AsTeamsChannelAccount();
            var teamsPerson2 = pair.Item2.AsTeamsChannelAccount();

            // Fill in person2's info in the card for person1
            var cardForPerson1 = PairUpNotificationAdaptiveCard.GetCard(teamName, teamsPerson2.Name, teamsPerson1.Name, teamsPerson2.GivenName, teamsPerson1.GivenName, teamsPerson1.GivenName, teamsPerson2.UserPrincipalName, this.botDisplayName);

            DateTime scheduledDate = DateTime.Now;
            var serviceURL = connectorClient.BaseUri.AbsoluteUri;
            await this.SavePairUpusers(string.Format(Resources.MeetupTitle, teamsPerson2.Name, teamsPerson1.Name), scheduledDate, teamsPerson2.UserPrincipalName, teamsPerson2.Name, teamsPerson1.Name, true, teamId, serviceURL);

            // Fill in person1's info in the card for person2
            var cardForPerson2 = PairUpNotificationAdaptiveCard.GetCard(teamName, teamsPerson1.Name, teamsPerson2.Name, teamsPerson1.GivenName, teamsPerson2.GivenName, teamsPerson2.GivenName, teamsPerson1.UserPrincipalName, this.botDisplayName);


            await this.SavePairUpusers(string.Format(Resources.MeetupTitle, teamsPerson1.Name, teamsPerson2.Name), scheduledDate, teamsPerson1.UserPrincipalName, teamsPerson1.Name, teamsPerson2.Name, true, teamId, serviceURL);

            // Send notifications and return the number that was successful
            var notifyResults = await Task.WhenAll(
                this.NotifyUser(connectorClient, cardForPerson1, teamsPerson1, tenantId),
                this.NotifyUser(connectorClient, cardForPerson2, teamsPerson2, tenantId));
            return notifyResults.Count(wasNotified => wasNotified);
        }

        /// <summary>
        /// Notify a pairup.
        /// </summary>
        /// <param name="connectorClient">The connector client</param>
        /// <param name="tenantId">The tenant id</param>
        /// <param name="teamName">The team name</param>
        /// <param name="pair">The pairup</param>
        /// <returns>Number of users notified successfully</returns>
        private async Task<int> FeedbackUser(ConnectorClient connectorClient, string tenantId, string teamName, List<ChannelAccount> pair)
        {
            this.telemetryClient.TrackTrace($"Sending pairup notification to {pair[0].Id} and {pair[1].Id}");

            var teamsPerson1 = pair[0].AsTeamsChannelAccount();
            var teamsPerson2 = pair[1].AsTeamsChannelAccount();
            var random = new Random().Next(1, 999999);

            var feedbackIdPerson1 = random.ToString();
            var feedbackIdPerson2 = (random + 1).ToString();

            // Fill in person2's info in the card for person1
            var cardForPerson1 = FeedbackAdaptiveCard.GetCard(teamsPerson2.Name, teamsPerson2.GivenName, teamsPerson2.UserPrincipalName, teamsPerson1.UserPrincipalName, feedbackIdPerson1);

            // Fill in person1's info in the card for person2
            var cardForPerson2 = FeedbackAdaptiveCard.GetCard(teamsPerson1.Name, teamsPerson1.GivenName, teamsPerson1.UserPrincipalName, teamsPerson2.UserPrincipalName, feedbackIdPerson2);

            // Send notifications and return the number that was successful
            var notifyResults = await Task.WhenAll(
                this.NotifyUser(connectorClient, cardForPerson1, teamsPerson1, tenantId),
                this.NotifyUser(connectorClient, cardForPerson2, teamsPerson2, tenantId));
            return notifyResults.Count(wasNotified => wasNotified);
        }

        private async Task<bool> NotifyUser(ConnectorClient connectorClient, string cardToSend, ChannelAccount user, string tenantId)
        {
            this.telemetryClient.TrackTrace($"Sending notification to user {user.Id}");

            try
            {
                // ensure conversation exists
                var bot = new ChannelAccount { Id = this.botId };
                var response = connectorClient.Conversations.CreateOrGetDirectConversation(bot, user, tenantId);
                this.telemetryClient.TrackTrace($"Received conversation {response.Id}");

                // construct the activity we want to post
                var activity = new Activity()
                {
                    Type = ActivityTypes.Message,
                    Conversation = new ConversationAccount()
                    {
                        Id = response.Id,
                    },
                    Attachments = new List<Attachment>()
                    {
                        new Attachment()
                        {
                            ContentType = "application/vnd.microsoft.card.adaptive",
                            Content = JsonConvert.DeserializeObject(cardToSend),
                        }
                    }
                };

                if (!this.isTesting)
                {
                    // shoot the activity over
                    await connectorClient.Conversations.SendToConversationAsync(activity);
                }

                return true;
            }
            catch (Exception ex)
            {
                this.telemetryClient.TrackTrace($"Error sending notification to user: {ex.Message}", SeverityLevel.Warning);
                this.telemetryClient.TrackException(ex);
                return false;
            }
        }

        /// <summary>
        /// Method that will send out the message in the General channel of the team
        /// that this bot has been installed to
        /// </summary>
        /// <param name="connectorClient">The connector client</param>
        /// <param name="cardToSend">The actual welcome card (for the team)</param>
        /// <param name="teamId">The team id</param>
        /// <returns>A tracking task</returns>
        private async Task NotifyTeam(ConnectorClient connectorClient, string cardToSend, string teamId)
        {
            this.telemetryClient.TrackTrace($"Sending notification to team {teamId}");

            try
            {
                var activity = new Activity()
                {
                    Type = ActivityTypes.Message,
                    Conversation = new ConversationAccount()
                    {
                        Id = teamId
                    },
                    Attachments = new List<Attachment>()
                    {
                        new Attachment()
                        {
                            ContentType = "application/vnd.microsoft.card.adaptive",
                            Content = JsonConvert.DeserializeObject(cardToSend)
                        }
                    }
                };

                await connectorClient.Conversations.SendToConversationAsync(activity);
            }
            catch (Exception ex)
            {
                this.telemetryClient.TrackTrace($"Error sending notification to team: {ex.Message}", SeverityLevel.Warning);
                this.telemetryClient.TrackException(ex);
            }
        }

        private async Task<List<ChannelAccount>> GetOptedInUsers(ConnectorClient connectorClient, TeamInstallInfo teamInfo)
        {
            // Pull the roster of specified team and then remove everyone who has opted out explicitly
            var members = await connectorClient.Conversations.GetConversationMembersAsync(teamInfo.TeamId);
            this.telemetryClient.TrackTrace($"Found {members.Count} in team {teamInfo.TeamId}");

            var tasks = members.Select(m => this.dataProvider.GetUserInfoAsync(m.AsTeamsChannelAccount().ObjectId));
            var results = await Task.WhenAll(tasks);

            return members
                .Zip(results, (member, userInfo) => ((userInfo == null) || userInfo.OptedIn) ? member : null)
                .Where(m => m != null)
                .ToList();
        }

        private List<Tuple<ChannelAccount, ChannelAccount>> MakePairs(List<ChannelAccount> users)
        {
            if (users.Count > 1)
            {
                this.telemetryClient.TrackTrace($"Making {users.Count / 2} pairs among {users.Count} users");
            }
            else
            {
                this.telemetryClient.TrackTrace($"Pairs could not be made because there is only 1 user in the team");
            }

            this.Randomize(users);

            var pairs = new List<Tuple<ChannelAccount, ChannelAccount>>();
            for (int i = 0; i < users.Count - 1; i += 2)
            {
                pairs.Add(new Tuple<ChannelAccount, ChannelAccount>(users[i], users[i + 1]));
            }

            return pairs;
        }

        private void Randomize<T>(IList<T> items)
        {
            Random rand = new Random(Guid.NewGuid().GetHashCode());

            // For each spot in the array, pick
            // a random item to swap into that spot.
            for (int i = 0; i < items.Count - 1; i++)
            {
                int j = rand.Next(i, items.Count);
                T temp = items[i];
                items[i] = items[j];
                items[j] = temp;
            }
        }
    }
}