// Talks to Segra's local control endpoint (Backend/Api/ControlApi.cs).
const BASE_URL = "http://localhost:2222/api/control";

export type SegraStatus = {
	recording: boolean;
	alwaysOnBuffer: boolean;
};

async function request(method: "GET" | "POST", path: string): Promise<unknown> {
	try {
		const response = await fetch(`${BASE_URL}/${path}`, {
			method,
			// Segra only accepts requests with this header; browsers can't send it cross-site without a preflight
			headers: { "X-Segra-Control": "1" },
			signal: AbortSignal.timeout(2000),
		});
		return response.ok ? await response.json() : null;
	} catch {
		// Segra isn't running
		return null;
	}
}

/** Segra's current status, or `null` when Segra isn't running. */
export async function getStatus(): Promise<SegraStatus | null> {
	return (await request("GET", "status")) as SegraStatus | null;
}

/** Runs an action in Segra; `true` when it did something. */
export async function runAction(path: string): Promise<boolean> {
	const result = (await request("POST", path)) as { done?: boolean } | null;
	return result?.done === true;
}
