import { KeyDownEvent, SingletonAction, WillAppearEvent } from "@elgato/streamdeck";

import { getStatus, runAction, SegraStatus } from "../segra";

/** Any key or dial showing one of this plugin's actions (the SDK's `Action` with default settings). */
export type SegraKey = WillAppearEvent["action"];

/** A key that runs one Segra action and shows whether Segra is running. */
export abstract class SegraAction extends SingletonAction {
	private static readonly all: SegraAction[] = [];
	// Last status shown on the keys; "" forces the next refresh to redraw them
	private static shownStatus = "";
	private static refreshing = false;

	protected abstract readonly path: string;

	constructor() {
		super();
		SegraAction.all.push(this);
	}

	/** Polls Segra and redraws the keys when its status changed. */
	static async refresh(): Promise<void> {
		if (SegraAction.refreshing || !SegraAction.all.some((a) => a.actions.length > 0)) return;

		SegraAction.refreshing = true;
		try {
			const status = await getStatus();
			const key = JSON.stringify(status);
			if (key === SegraAction.shownStatus) return;

			SegraAction.shownStatus = key;
			for (const segraAction of SegraAction.all) {
				for (const action of segraAction.actions) {
					await segraAction.show(action, status);
				}
			}
		} finally {
			SegraAction.refreshing = false;
		}
	}

	override async onWillAppear(_ev: WillAppearEvent): Promise<void> {
		SegraAction.shownStatus = "";
		await SegraAction.refresh();
	}

	override async onKeyDown(ev: KeyDownEvent): Promise<void> {
		if (await runAction(this.path)) {
			await ev.action.showOk();
		} else {
			await ev.action.showAlert();
		}
		await SegraAction.refresh();
	}

	/** Draws one key for `status`; `null` means Segra isn't running. */
	protected async show(action: SegraKey, status: SegraStatus | null): Promise<void> {
		if (action.isKey()) await action.setTitle(status ? undefined : "離線");
	}
}
