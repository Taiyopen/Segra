import { action } from "@elgato/streamdeck";

import { SegraAction } from "./segra-action";

/** Saves the replay buffer. */
@action({ UUID: "com.taiyopen.segra.save-replay" })
export class SaveReplay extends SegraAction {
	protected readonly path = "save-replay";
}
