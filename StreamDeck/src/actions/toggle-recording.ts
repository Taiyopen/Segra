import { action } from "@elgato/streamdeck";

import { SegraStatus } from "../segra";
import { SegraAction, SegraKey } from "./segra-action";

/** Starts or stops recording; the key turns red while recording. */
@action({ UUID: "com.taiyopen.segra.toggle-recording" })
export class ToggleRecording extends SegraAction {
	protected readonly path = "toggle-recording";

	protected override async show(action: SegraKey, status: SegraStatus | null): Promise<void> {
		await super.show(action, status);
		if (action.isKey()) await action.setState(status?.recording ? 1 : 0);
	}
}
