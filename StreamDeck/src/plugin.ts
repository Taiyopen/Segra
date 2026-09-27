import streamDeck from "@elgato/streamdeck";

import { AlwaysOnBuffer } from "./actions/always-on-buffer";
import { Bookmark } from "./actions/bookmark";
import { SaveReplay } from "./actions/save-replay";
import { SegraAction } from "./actions/segra-action";
import { ToggleRecording } from "./actions/toggle-recording";

streamDeck.actions.registerAction(new ToggleRecording());
streamDeck.actions.registerAction(new SaveReplay());
streamDeck.actions.registerAction(new Bookmark());
streamDeck.actions.registerAction(new AlwaysOnBuffer());

// Keep the keys in step with Segra (recording, always-on buffer, offline)
setInterval(() => void SegraAction.refresh(), 1000);

streamDeck.connect();
