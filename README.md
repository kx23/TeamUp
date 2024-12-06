# TeamUp - Discord Event Bot

### The bot is temporarily unavailable

A simple Discord bot to organize events on your server. Users can create events, join them, and confirm their readiness. The event will start when all participants are ready or the required number of participants is reached.

## Features
- Create events with a required number of participants.
- Users can join, leave, and confirm readiness.
- When all participants confirm their readiness, everyone receives a notification about the meeting place (the name of the voice channel).
- If time expires or the required participants aren’t met, the event is canceled.

## Getting Started

### 1. Add the Bot to Your Server
1. Click the link to invite the bot to your server:  
   [Add Bot to Your Server](https://discord.com/oauth2/authorize?client_id=1309848666546110464)
2. Select the server and grant the necessary permissions.

### 2. Create event
To create an event, use the following command in any text channel:
```
!create_event EventName RequiredParticipants DurationInMinutes "Voice Channel Name"
```

Example:
```
!create_event Test Event 3 15 "Gaming Voice Channel"
```

This will create an event called "Test Event" with:

- 3 required participants
- 15 minutes duration 
- Voice channel named "Gaming Voice Channel"
