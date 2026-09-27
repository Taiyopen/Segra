import { action } from "@elgato/streamdeck";

import { SegraStatus } from "../segra";
import { SegraAction, SegraKey } from "./segra-action";

/** Turns the always-on replay buffer on or off; the key lights up while it's on. */
@action({ UUID: "com.taiyopen.segra.always-on-buffer" })
export class AlwaysOnBuffer extends SegraAction {
	protected readonly path = "toggle-always-on-buffer";

	protected override async show(action: SegraKey, status: SegraStatus | null): Promise<void> {
		await super.show(action, status);
		if (action.isKey()) await action.setState(status?.alwaysOnBuffer ? 1 : 0);
	}
}
