import { action } from "@elgato/streamdeck";

import { SegraAction } from "./segra-action";

/** Adds a bookmark to the current recording. */
@action({ UUID: "com.taiyopen.segra.bookmark" })
export class Bookmark extends SegraAction {
	protected readonly path = "bookmark";
}
