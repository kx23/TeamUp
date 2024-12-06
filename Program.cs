using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TeamUp
{
    internal class Program
    {
        static DiscordClient _client;
        static ConcurrentDictionary<ulong, EventData> ActiveEvents = new();

        static async Task Main(string[] args)
        {
            string tokenFilePath = "C:\\Users\\Admin\\source\\repos\\TeamUp\\bot_token.txt";
            string token = GetTokenFromFile(tokenFilePath);
            if (string.IsNullOrEmpty(token))
            {
                Console.WriteLine("Error: Token not found. Create a file 'bot_token.txt' and put your bot token inside.");
                return;
            }

            _client = new DiscordClient(new DiscordConfiguration
            {
                Token = token,
                TokenType = TokenType.Bot,
                Intents = DiscordIntents.AllUnprivileged | DiscordIntents.GuildMembers | DiscordIntents.MessageContents,
                MinimumLogLevel = LogLevel.Debug
            });

            _client.UseInteractivity();

            _client.MessageCreated += OnMessageCreated;
            await _client.ConnectAsync();
            Console.WriteLine("Bot is running!");
            await Task.Delay(-1);
        }

        /// <summary>
        /// Reads the token from a text file.
        /// </summary>
        /// <param name="filePath">The path to the token file.</param>
        /// <returns>The token or null if the file does not exist or is empty.</returns>
        private static string GetTokenFromFile(string filePath)
        {
            string solutionRoot = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.FullName;
            string tokenFilePath = Path.Combine(solutionRoot, filePath);

            if (File.Exists(tokenFilePath))
            {
                return File.ReadAllText(tokenFilePath).Trim();
            }

            return null;
        }

        private static async Task OnMessageCreated(DiscordClient client, MessageCreateEventArgs e)
        {
            if (e.Message.Content.StartsWith("!create_event"))
            {
                await HandleEventCreation(e);
            }
        }

        private static async Task HandleEventCreation(MessageCreateEventArgs e)
        {
            try
            {
                var args = Regex.Matches(e.Message.Content, @"[\""].+?[\""]|[^ ]+")
                        .Select(m => m.Value.Trim('"'))
                        .ToArray();
                if (args.Length < 4)
                {
                    await e.Message.RespondAsync("Error: Insufficient parameters. Use: `!create_event EventName MinParticipants MaxParticipants DurationInSeconds VoiceChannel`");
                    Console.WriteLine("Not enough parameters");
                    return;
                }

                string eventName = args[1];
                int requiredParticipants = int.Parse(args[2]);
                int duration = int.Parse(args[3]);
                var voiceChannelName = args[4];
                var voiceChannel = e.Guild.Channels.Values.FirstOrDefault(c => c.Name == voiceChannelName && c.Type == ChannelType.Voice);

                if (voiceChannel == null)
                {
                    await e.Message.RespondAsync($"Error: Voice channel with the name `{voiceChannelName}` not found.");
                    return;
                }

                // Create buttons
                var joinButton = new DiscordButtonComponent(ButtonStyle.Primary, "join_event", "Join");
                var leaveButton = new DiscordButtonComponent(ButtonStyle.Danger, "leave_event", "Leave");
                var msg = await e.Message.RespondAsync(new DiscordMessageBuilder()
                    .WithContent($"🎮 **{eventName}**\nRequired participants: {requiredParticipants}.\nClick the button to join!")
                    .AddComponents(joinButton, leaveButton));

                // Create event data and add it to the collection
                var eventData = new EventData
                {
                    EventName = eventName,
                    RequiredParticipants = requiredParticipants,
                    EndTime = duration > 0 ? DateTime.UtcNow.AddMinutes(duration) : DateTime.MaxValue,
                    Message = msg,
                    VoiceChannel = voiceChannel
                };

                if (!ActiveEvents.TryAdd(msg.Id, eventData))
                {
                    await e.Message.RespondAsync("Failed to create the event. Please try again.");
                    return;
                }

                // Start event processing
                _ = Task.Run(() => ProcessEvent(eventData, e.Guild));
                Console.WriteLine($"{eventName} event task has been created and launched.");
            }
            catch (Exception ex)
            {
                await e.Message.RespondAsync($"Error: {ex.Message}");
            }
        }

        private static async Task ProcessEvent(EventData eventData, DiscordGuild guild)
        {
            var interactivity = _client.GetInteractivity();
            var lastParticipantCount = 0;

            while (DateTime.UtcNow < eventData.EndTime)
            {
                var buttonEvent = await interactivity.WaitForButtonAsync(eventData.Message, TimeSpan.FromSeconds(1));

                if (buttonEvent.TimedOut)
                    continue;

                var user = await guild.GetMemberAsync(buttonEvent.Result.User.Id);
                await buttonEvent.Result.Interaction.CreateResponseAsync(
        InteractionResponseType.DeferredMessageUpdate);
                switch (buttonEvent.Result.Interaction.Data.CustomId)
                {
                    case "join_event":
                        if (!eventData.Participants.Contains(user))
                        {
                            eventData.Participants.Add(user);
                            
                        }
                        break;

                    case "leave_event":
                        if (eventData.Participants.Contains(user))
                        {
                            eventData.Participants.Remove(user);
                        }
                        break;
                }

                // Update the message only if the participant list changes
                if (eventData.Participants.Count != lastParticipantCount)
                {
                    await UpdateEventMessage(eventData);
                    lastParticipantCount = eventData.Participants.Count;
                }

                // If enough participants are gathered, check readiness
                if (eventData.Participants.Count >= eventData.RequiredParticipants)
                {
                    bool allReady = await CheckReadiness(eventData.Participants, eventData.Message, eventData.RequiredParticipants);
                    if (allReady)
                    {
                        await NotifyParticipants(eventData.Participants, eventData.VoiceChannel);

                        var content = $"🎮 **{eventData.EventName}**\n" +
              $"Participants ({eventData.Participants.Count}/{eventData.RequiredParticipants}):\n" +
              (eventData.Participants.Count > 0
                  ? string.Join("\n", eventData.Participants.Select(p => $"- {p.DisplayName}"))
                  : "No participants yet.");
                        content += "\n" + "The event has started!";

                        await eventData.Message.ModifyAsync(new DiscordMessageBuilder()
                .WithContent(content));

                        break; // Stop the loop once everyone is ready
                    }
                    else
                    {
                        await UpdateEventMessage(eventData);
                    }
                }
            }

            // Finalize the event if time runs out
            if (DateTime.UtcNow >= eventData.EndTime)
            {
                var content =  $"{eventData.EventName} event time has ended. The event is now closed.";
                await eventData.Message.ModifyAsync(new DiscordMessageBuilder()
                .WithContent(content));

            }

            // Delete the message and remove the event from the collection
            //await eventData.Message.DeleteAsync();
            ActiveEvents.TryRemove(eventData.Message.Id, out _);
        }

        private static async Task UpdateEventMessage(EventData eventData)
        {
            var content = $"🎮 **{eventData.EventName}**\n" +
                          $"Participants ({eventData.Participants.Count}/{eventData.RequiredParticipants}):\n" +
                          (eventData.Participants.Count > 0
                              ? string.Join("\n", eventData.Participants.Select(p => $"- {p.DisplayName}"))
                              : "No participants yet.");

            var joinButton = new DiscordButtonComponent(ButtonStyle.Primary, "join_event", "Join");
            var leaveButton = new DiscordButtonComponent(ButtonStyle.Danger, "leave_event", "Leave");

            await eventData.Message.ModifyAsync(new DiscordMessageBuilder()
                .WithContent(content)
                .AddComponents(joinButton, leaveButton));
        }

        private static async Task NotifyParticipants(List<DiscordMember> participants, DiscordChannel voiceChannel)
        {
            foreach (var participant in participants)
            {
                try
                {
                    var dmChannel = await participant.CreateDmChannelAsync();
                    await dmChannel.SendMessageAsync($"The event has started! Please join the voice channel \"{voiceChannel.Name}\".");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending DM to {participant.DisplayName}: {ex.Message}");
                }
            }
        }

        private static async Task<bool> CheckReadiness(List<DiscordMember> participants, DiscordMessage eventMessage, int requiredParticipants)
        {
            var interactivity = _client.GetInteractivity();
            var readyParticipants = new List<DiscordMember>();

            foreach (var participant in participants.ToList()) // Iterate through a copy to allow modification
            {
                try
                {
                    var dmChannel = await participant.CreateDmChannelAsync();
                    var readyButton = new DiscordButtonComponent(ButtonStyle.Success, $"ready_{participant.Id}", "Ready");
                    var message = await dmChannel.SendMessageAsync(new DiscordMessageBuilder()
                        .WithContent("Please confirm your readiness for the event!")
                        .AddComponents(readyButton));

                    // Wait for the participant to press the button
                    var buttonEvent = await interactivity.WaitForButtonAsync(message, TimeSpan.FromSeconds(10));

                    if (!buttonEvent.TimedOut && buttonEvent.Result.User.Id == participant.Id)
                    {
                        readyParticipants.Add(participant);
                        await buttonEvent.Result.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                            new DiscordInteractionResponseBuilder().WithContent("You are ready!"));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending DM to {participant.DisplayName}: {ex.Message}");
                }
            }

            // Remove unready participants
            foreach (var participant in participants.Except(readyParticipants).ToList())
            {
                participants.Remove(participant);
                await eventMessage.Channel.SendMessageAsync($"{participant.DisplayName} event has been canceled because the required number of participants was not reached and the time expired.");
            }

            // Return true if there are enough ready participants
            return readyParticipants.Count >= requiredParticipants;
        }
    }

    public class EventData
    {
        public string EventName { get; set; }
        public List<DiscordMember> Participants { get; set; } = new List<DiscordMember>();
        public int RequiredParticipants { get; set; }
        public DateTime EndTime { get; set; }
        public DiscordMessage Message { get; set; }
        public DiscordChannel VoiceChannel { get; set; }
    }
}
