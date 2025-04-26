# Cliparino

Streamer Bot clip player for Twitch.tv.

## Features

Has feature parity with other popular clip players, including:

- [x] Play specific clips from Twitch.tv
- [x] Enqueue clips for playback
- [x] Play clips other users post in chat
- [x] Play random clips from Twitch.tv for shoutouts, either by command or automatically during raids... oh yeah,
  and it does the shoutout too (both a configurable text-based shoutout and the slash command).
- [x] Stop clip playback
- [x] Configure clip player settings

and even goes a bit further, allowing you to:

- [x] Play clips by name using fuzzy search!

## Installation

1. Make sure to have the latest version of Streamer.bot installed and set up! (Tested with 0.2.6)
2. Download the [latest release](https://github.com/angrmgmt/Cliparino/releases/latest) from
   the [Releases](https://github.com/angrmgmt/Cliparino/releases).
3. From Streamer.bot, import the Cliparino.sb file by dragging it into the Streamer Bot window's Import section and
   dropping it on the "Import String" box.
4. Adjust settings as needed in the "Cliparino" _Action_ under _Sub-Actions_, make sure your new actions under
   the "Cliparino" group are enabled, and do the same for commands.
5. Enjoy!

## Usage

### Playing Clips

- Either `!watch <clip-link>` or `!watch` after a clip link has been posted in chat will play the clip. If it doesn't
  exist already, a scene and source will be created and included in your current scene. This scene source can be
  copied and pasted to other scenes as desired.
- Using `!watch @broadcasterName some words to search for` will search for clips on that channel that best match the
  provided search terms.
  Omitting the broadcaster's name will search your channel's clips.
  By default, this is gated by moderator approval.
  The terms available to approve or deny are pretty exhaustive, so it should be as intuitive as answering naturally.

  **Important:** *You ***must*** use the `@` symbol before the username with this version of the command.*
- If multiple clips are enqueued by sequential commands in chat, they will play in order.
- Shoutouts also have their own queue and will play in the order they are received.

### Shoutouts

- `!so <username>` will play a random clip from the specified user's Twitch channel, starting with Featured clips,
  then moving to a configurable set of ranges. This can be set to fire automatically when a raid is detected, which is
  the default.
  See the last item in [Settings](#settings).
  It will also provide a message telling chat what they were playing and provide a link to the channel, encouraging
  your viewers to go be their viewers sometime.
  Finally, it will use the built-in twitch `/shoutout` command to give everyone an easy button to press for follows.

### Replay

- `!replay` will play the most recently played clip again, just as good as the first time.

### Stop

- `!stop` will stop the current clip playback.
  The next clip in the queue will be played after a delay.
  This delay is unintended but should be fixed in a future hotfix.

### Settings

- All settings are configurable in the Cliparino _Action_ under _Sub-Actions_. These include:
    - The scene and player screen dimensions, default value of 1920x1080. Users can select whatever (width)x(height)
      they want, and the player will auto-adjust to 16:9, leaving horizontal or vertical black bars as needed.
    - Logging for debugging purposes. This will log all clip playback events to a file in the Streamer Bot Log folder.
  - A message to be sent to chat while performing a shoutout, with a link to their Twitch channel.
    This can be edited if the message isn't to your liking.
    Alternatively, It can be ~~~~disabled by setting its value to `""`.
    - A toggle for featured clips to be played during shoutouts. If disabled, any clip from the user's channel will be
      played.
    - The max length of clip to play in seconds during shoutouts, defaulting to 30 seconds. Clips longer than this will
      be omitted.
  - The maximum age of a clip to play in days during shoutouts, defaulting to 30 days.
    Clips older than this will be omitted.
- Automatic Shoutouts are handled by a separate _Action_, which can easily be disabled if not desired. The _Action_
  consists of a single trigger and single _Sub-Action_ which itself triggers Cliparino's shoutout functionality.

## Issues

If you encounter any issues,
please report them in the [Issues](https://github.com/angrmgmt/Cliparino/issues) tab on GitHub.
Please include as much information as possible, including the Streamer.bot log file if possible.

## Contributing

The files in the `src` and `src/Managers` directories of the Cliparino project folder constitute the primary code
portion of the app.
The only difference between them and what is in the .sb file is all the documentation comments and linter directives
and stuff that Streamer.bot doesn't really know what to do with.
Speaking of which, feel free to set up automation making use of the FileProcessor project's FileCleaner app, which
will make the Streamer.bot-friendly file automatically for you.
I used the `dotnet run` approach, but you do you.

Pull requests are welcome.
For major changes, please open an issue first to discuss what you would like to change.

For your convenience, here are a couple of templates to use. Enjoy!

- [Bugfix](https://github.com/angrmgmt/Cliparino/compare?title=Bugfix%20Request&body=%23%23%20Bugfix%20Pull%20Request%0A%0A%23%23%23%20Description%0APlease%20provide%20a%20clear%20and%20concise%20description%20of%20the%20bug%20and%20the%20fix.%0A%0A%23%23%23%20Related%20Issue%0AIf%20applicable%2C%20please%20provide%20a%20link%20to%20the%20related%20issue.%0A%0A%23%23%23%20How%20Has%20This%20Been%20Tested%3F%0APlease%20describe%20the%20tests%20that%20you%20ran%20to%20verify%20your%20changes.%20Provide%20instructions%20so%20we%20can%20reproduce.%0A%0A-%20%5B%20%5D%20Test%20A%0A-%20%5B%20%5D%20Test%20B%0A%0A%23%23%23%20Screenshots%20(if%20appropriate)%3A%0AIf%20applicable%2C%20add%20screenshots%20to%20help%20explain%20your%20problem%20and%20solution.%0A%0A%23%23%23%20Checklist%3A%0A-%20%5B%20%5D%20My%20code%20follows%20the%20style%20guidelines%20of%20this%20project%0A-%20%5B%20%5D%20I%20have%20performed%20a%20self-review%20of%20my%20own%20code%0A-%20%5B%20%5D%20I%20have%20commented%20my%20code%2C%20particularly%20in%20hard-to-understand%20areas%0A-%20%5B%20%5D%20I%20have%20made%20corresponding%20changes%20to%20the%20documentation%0A-%20%5B%20%5D%20My%20changes%20generate%20no%20new%20warnings%0A-%20%5B%20%5D%20I%20have%20added%20tests%20that%20prove%20my%20fix%20is%20effective%20or%20that%20my%20feature%20works%0A-%20%5B%20%5D%20New%20and%20existing%20unit%20tests%20pass%20locally%20with%20my%20changes%0A-%20%5B%20%5D%20Any%20dependent%20changes%20have%20been%20merged%20and%20published%20in%20downstream%20modules)
- [Feature](https://github.com/angrmgmt/Cliparino/compare?title=New%20Feature%20Request&body=%23%23%20Feature%3A%20%5BFeature%20Name%5D%0A%0A%23%23%23%20Description%0AProvide%20a%20detailed%20description%20of%20the%20feature%20being%20implemented.%20Include%20the%20purpose%20and%20functionality%20of%20the%20feature.%0A%0A%23%23%23%20Related%20Issue%0AIf%20applicable%2C%20mention%20any%20related%20issues%20or%20link%20to%20the%20issue%20number.%0A%0A%23%23%23%20Implementation%20Details%0ADescribe%20how%20the%20feature%20was%20implemented.%20Include%20information%20about%20any%20new%20files%2C%20functions%2C%20or%20changes%20to%20existing%20code.%0A%0A%23%23%23%20Testing%0AExplain%20how%20the%20feature%20was%20tested.%20Include%20details%20about%20any%20unit%20tests%2C%20integration%20tests%2C%20or%20manual%20testing%20performed.%0A%0A%23%23%23%20Checklist%0A-%20%5B%20%5D%20I%20have%20performed%20a%20self-review%20of%20my%20own%20code%0A-%20%5B%20%5D%20I%20have%20commented%20my%20code%2C%20particularly%20in%20hard-to-understand%20areas%0A-%20%5B%20%5D%20I%20have%20made%20corresponding%20changes%20to%20the%20documentation%0A-%20%5B%20%5D%20I%20have%20added%20tests%20that%20prove%20my%20fix%20is%20effective%20or%20that%20my%20feature%20works%0A-%20%5B%20%5D%20New%20and%20existing%20unit%20tests%20pass%20locally%20with%20my%20changes%0A-%20%5B%20%5D%20Any%20dependent%20changes%20have%20been%20merged%20and%20published%20in%20downstream%20modules%0A%0A%23%23%23%20Screenshots%20(if%20applicable)%0AIf%20applicable%2C%20add%20screenshots%20to%20help%20explain%20your%20feature.)
