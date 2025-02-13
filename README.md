# Cliparino

Streamer Bot clip player for Twitch.tv.

## Features

Has feature parity with other popular clip players, including:

- [x] Play specific clips from Twitch.tv
- [x] Enqueue clips for playback
- [x] Play clips other users post in chat
- [x] Play random clips from Twitch.tv for shoutouts, either by command or automatically during raids
- [x] Stop clip playback
- [x] Configure clip player settings

## Installation (WIP)

1. Download the latest release from the Releases (link to be added).
2. From Streamer.bot, import the Cliparino.sb file by dragging it into the Streamer Bot window's Import section, and
   dropping it on the "Import String" box.
3. Adjust settings as needed in the "Cliparino" _Action_ under _Sub-Actions_.

## Usage

### Playing Clips

- Either `!watch <clip-link>` or `!watch` after a clip link has been posted in chat will play the clip. If it doesn't
  exist already, a scene and source will be created and included in your current scene. This scene source can be
  copy/pasted to other scenes as desired.
- If multiple clips are enqueued by sequential commands in chat, they will play in order.
- Shoutouts also have their own queue, and will play in the order they are received.

### Shoutouts

- `!so <username>` will play a random clip from the specified user's Twitch channel, starting with Featured clips,
  then moving to a configurable set of ranges. This can be set to fire automatically when a raid is detected, which is
  the default. See the last item in [Settings](#settings)

### Stop

- `!stop` will stop the current clip playback. The next clip in the queue will be played.

### Settings

- All settings are configurable in the Cliparino _Action_ under _Sub-Actions_. These include:
    - The scene and player screen dimensions, default value of 1920x1080. Users can select whatever (width)x(height)
      they want, and the player will auto-adjust to 16:9, leaving horizontal or vertical black bars as needed.
    - Logging for debugging purposes. This will log all clip playback events to a file in the Streamer Bot Log folder.
    - A message to be sent to chat while performing a shoutout, with a link to their Twitch channel. This can be
      disabled by setting its value to `""`.
    - A toggle for featured clips to be played during shoutouts. If disabled, any clip from the user's channel will be
      played.
    - The max length of clip to play in seconds during shoutouts, defaulting to 30 seconds. Clips longer than this will
      be omitted.
    - The maximum age of clip to play in days during shoutouts, defaulting to 30 days. Clips older than this will be
      omitted.
- Automatic Shoutouts are handled by a separate _Action_, which can easily be disabled if not desired. The _Action_
  consists of a single trigger and single _Sub-Action_ which itself triggers Cliparino's shoutout functionality.

## Issues

If you encounter any issues, please report them in the Issues tab on GitHub. Please include as much information as
possible, including the Streamer.bot log file if possible.

## Contributing

The files in the main directory of the Cliparino project folder constitute the primary code portion of the app. The only
difference between them and what is in the .sb file is global usings, as most linters yeet them by default.

Pull requests are welcome. For major changes, please open an issue first to discuss what you would like to change.
