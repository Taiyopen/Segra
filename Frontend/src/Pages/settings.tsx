import React, {useEffect, useState} from 'react';
import {useSettings, useSettingsUpdater} from '../Context/SettingsContext';
import {sendMessageToBackend} from '../Utils/MessageUtils';
import {themeChange} from 'theme-change';
import {AudioDevice} from '../Models/types';
import {supabase} from '../lib/supabase/client';
import {FaDiscord} from 'react-icons/fa';
import {useAuth} from '../Hooks/useAuth.tsx';
import {useProfile} from '../Hooks/useUserProfile';
import {MdOutlineLogout, MdWarning, MdLock} from 'react-icons/md';
import {useUpdate} from '../Context/UpdateContext';

export default function Settings() {
	const {session, authError, isAuthenticating, clearAuthError, signOut} = useAuth();
	const {data: profile, error: profileError} = useProfile();
	const [error, setError] = useState('');
	const [email, setEmail] = useState('');
	const [password, setPassword] = useState('');
	const {openReleaseNotesModal} = useUpdate();
	const settings = useSettings();
	const updateSettings = useSettingsUpdater();
	const [localStorageLimit, setLocalStorageLimit] = useState<number>(settings.storageLimit);
	const [isLoggingOut, setIsLoggingOut] = useState(false);

	// Set error from auth if present
	useEffect(() => {
		if (authError) {
			setError(authError);
			clearAuthError();
		}
	}, [authError, clearAuthError]);

	// Rest of your existing settings logic
	useEffect(() => {
		setLocalStorageLimit(settings.storageLimit);
	}, [settings.storageLimit]);

	const handleChange = (event: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => {
		const {name, value} = event.target;
		const numericalFields = ['frameRate', 'bitrate', 'storageLimit', 'keyframeInterval', 'crfValue', 'cqLevel'];
		updateSettings({
			[name]: numericalFields.includes(name) ? Number(value) : value,
		});
	};

	const handleBrowseClick = () => {
		sendMessageToBackend('SetVideoLocation');
	};

	useEffect(() => {
		themeChange(false);
	}, []);

	// Updated Discord login handler
	const handleDiscordLogin = async () => {
		setError('');
		try {
			const {error} = await supabase.auth.signInWithOAuth({
				provider: 'discord',
				options: {
					redirectTo: window.location.href,
					queryParams: {prompt: 'consent'}
				}
			});

			if (error) throw error;
		} catch (err) {
			setError(err instanceof Error ? err.message : 'Failed to start authentication');
		}
	};

	const handleEmailLogin = async (e: React.FormEvent) => {
		e.preventDefault();
		setError('');
		try {
			const {error} = await supabase.auth.signInWithPassword({email, password});
			if (error) throw error;
		} catch (err) {
			setError(err instanceof Error ? err.message : 'Login failed');
		}
	};

	const handleLogout = async () => {
		try {
			setIsLoggingOut(true);
			// Use the AuthProvider's signOut method to ensure UI is updated
			await signOut();
		} catch (err) {
			console.error("Logout failed:", err);
			setError(err instanceof Error ? err.message : 'Logout failed');
		} finally {
			setIsLoggingOut(false);
		}
	};

	// Render the authentication section based on login state
	const renderAuthSection = () => {
		if (!session) {
			return (
				<div className="p-4 bg-base-300 rounded-lg shadow-md">
					<h2 className="text-xl font-semibold mb-4">Authentication</h2>
					
					{error && (
						<div className="alert alert-error mb-4" role="alert">
							<MdWarning className="w-5 h-5" />
							<span>{error}</span>
						</div>
					)}
					
					<div className="bg-base-100 p-6 rounded-lg space-y-4">
						<button
							onClick={handleDiscordLogin}
							disabled={isAuthenticating}
							className={`btn btn-neutral w-full gap-2 font-semibold text-white ${isAuthenticating ? 'btn-loading' : ''}`}
						>
							<FaDiscord className="w-5 h-5" />
							{isAuthenticating ? 'Connecting...' : 'Continue with Discord'}
						</button>

						<div className="divider">or use email</div>

						<form onSubmit={handleEmailLogin} className="space-y-4">
							<div className="form-control">
								<label className="label">
									<span className="label-text">Email</span>
								</label>
								<input
									type="email"
									value={email}
									onChange={(e) => setEmail(e.target.value)}
									className="input input-bordered"
									disabled={isAuthenticating}
									required
								/>
							</div>

							<div className="form-control">
								<label className="label">
									<span className="label-text">Password</span>
								</label>
								<input
									type="password"
									value={password}
									onChange={(e) => setPassword(e.target.value)}
									className="input input-bordered"
									disabled={isAuthenticating}
									required
								/>
							</div>

							<button
								type="submit"
								disabled={isAuthenticating}
								className={`btn btn-neutral w-full font-semibold text-white ${isAuthenticating ? 'btn-loading' : ''}`}
							>
								Sign in with Email
							</button>
						</form>
					</div>
				</div>
			);
		}
		
		return (
			<div className="p-4 bg-base-300 rounded-lg shadow-md">
				<h2 className="text-xl font-semibold mb-4">Account</h2>
				
				<div className="bg-base-100 p-4 rounded-lg">
					<div className="flex items-center justify-between flex-wrap gap-4">
						<div className="flex items-center gap-4 min-w-0">
							{/* Avatar Container */}
							<div className="relative w-16 h-16">
								<div className="w-full h-full rounded-full overflow-hidden bg-base-200 ring-2 ring-base-300">
									{profile?.avatar_url ? (
										<img
											src={profile.avatar_url}
											alt={`${profile.username}'s avatar`}
											className="w-full h-full object-cover"
											onError={(e) => {
												(e.target as HTMLImageElement).src = '/default-avatar.png';
											}}
										/>
									) : (
										<div
											className="w-full h-full bg-base-300 flex items-center justify-center"
											aria-hidden="true"
										>
											<span className="text-2xl"></span>
										</div>
									)}
								</div>
							</div>

							{/* Profile Info */}
							<div className="min-w-0 flex-1">
								<h3 className="font-bold truncate">
									{profile?.username ? (
										profile.username
									) : (
										<div className="skeleton h-[24px] w-24"></div>
									)}
								</h3>
								<p className="text-sm opacity-70 truncate">
									{session?.user?.email || 'Authenticated User'}
								</p>
							</div>
							
							{/* Logout Button */}
							<button
								onClick={handleLogout}
								className="btn btn-sm no-animation btn-outline btn-error"
								disabled={isLoggingOut}
							>
								{!isLoggingOut && <MdOutlineLogout className="w-4 h-4" />}
								{isLoggingOut ? 'Logging out...' : 'Logout'}
							</button>
						</div>
					</div>
					
					{/* Error State */}
					{profileError && (
						<div
							className="alert alert-error mt-3"
							role="alert"
							aria-live="assertive"
						>
							<MdWarning className="w-5 h-5" />
							<div>
								<h3 className="font-bold">Profile load failed!</h3>
								<div className="text-xs">{profileError.message || 'Unknown error occurred'}</div>
							</div>
						</div>
					)}
				</div>
			</div>
		);
	};

	// Helper function to check if the selected device is available
	const isDeviceAvailable = (deviceId: string, devices: AudioDevice[]) => {
		return devices.some((device) => device.id === deviceId);
	};

	// Get the name of the selected input device, or indicate if it's unavailable
	const selectedInputDevice = settings.state.inputDevices.find((device) => device.id === settings.inputDevice);
	const inputDeviceName = selectedInputDevice ? selectedInputDevice.name : settings.inputDevice ? 'Unavailable Device' : 'Select Input Device';

	// Get the name of the selected output device, or indicate if it's unavailable
	const selectedOutputDevice = settings.state.outputDevices.find((device) => device.id === settings.outputDevice);
	const outputDeviceName = selectedOutputDevice ? selectedOutputDevice.name : settings.outputDevice ? 'Unavailable Device' : 'Select Output Device';
	
	return (
		<div className="p-5 space-y-6 rounded-lg">
			<h1 className="text-3xl font-bold">Settings</h1>
			
			{/* Authentication Section */}
			{renderAuthSection()}

			{/* Segra AI Settings */}
			<div className="p-4 bg-base-300 rounded-lg shadow-md">
				<h2 className="text-xl font-semibold mb-4">Segra AI</h2>
				<div className="bg-base-100 p-4 rounded-lg">
					{!session && (
						<div className="flex items-center gap-2 mb-3 text-sm text-warning">
							<MdLock className="w-4 h-4" />
							<span>Sign in to access AI features</span>
						</div>
					)}
					<div className="flex items-center justify-between">
						<div className="flex items-center gap-2">
							<span className="font-medium">Enable Segra AI</span>
						</div>
						<input
							type="checkbox"
							name="enableAI"
							checked={settings.enableAi}
							onChange={(e) => updateSettings({enableAi: e.target.checked})}
							className="toggle"
							disabled={!session}
						/>
					</div>
				</div>
			</div>

			{/* Video Settings */}
			<div className="p-4 bg-base-300 rounded-lg shadow-md">
				<h2 className="text-xl font-semibold mb-4">Video Settings</h2>
				<div className="grid grid-cols-2 gap-4">
					{/* Resolution */}
					<div className="form-control">
						<label className="label">
							<span className="label-text">Resolution</span>
						</label>
						<select
							name="resolution"
							value={settings.resolution}
							onChange={handleChange}
							className="select select-bordered"
						>
							<option value="720p">720p</option>
							<option value="1080p">1080p</option>
							<option value="1440p">1440p</option>
							<option value="4K">4K</option>
						</select>
					</div>

					{/* Frame Rate */}
					<div className="form-control">
						<label className="label">
							<span className="label-text">Frame Rate (FPS)</span>
						</label>
						<select
							name="frameRate"
							value={settings.frameRate}
							onChange={handleChange}
							className="select select-bordered"
						>
							<option value="24">24</option>
							<option value="30">30</option>
							<option value="60">60</option>
							<option value="120">120</option>
							<option value="144">144</option>
						</select>
					</div>

					{/* Rate Control */}
					<div className="form-control">
						<label className="label">
							<span className="label-text">Rate Control</span>
						</label>
						<select
							name="rateControl"
							value={settings.rateControl}
							onChange={handleChange}
							className="select select-bordered"
						>
							<option value="CBR">CBR (Constant Bitrate)</option>
							<option value="VBR">VBR (Variable Bitrate)</option>
							<option value="CRF">CRF (Constant Rate Factor)</option>
							<option value="CQP">CQP (Constant Quantization Parameter)</option>
						</select>
					</div>

					{/* Bitrate (for CBR and VBR) */}
					{(settings.rateControl === 'CBR' || settings.rateControl === 'VBR') && (
						<div className="form-control">
							<label className="label">
								<span className="label-text">Bitrate (Mbps)</span>
							</label>
							<select
								name="bitrate"
								value={settings.bitrate}
								onChange={handleChange}
								className="select select-bordered"
							>
								{Array.from({length: 19}, (_, i) => (i + 2) * 5).map((value) => (
									<option key={value} value={value}>
										{value} Mbps
									</option>
								))}
							</select>
						</div>
					)}

					{/* CRF Value (for CRF) */}
					{settings.rateControl === 'CRF' && (
						<div className="form-control">
							<label className="label">
								<span className="label-text">CRF Value (0-51)</span>
							</label>
							<input
								type="number"
								name="crfValue"
								value={settings.crfValue}
								onChange={handleChange}
								min="0"
								max="51"
								className="input input-bordered"
							/>
						</div>
					)}

					{/* CQ Level (for CQP) */}
					{settings.rateControl === 'CQP' && (
						<div className="form-control">
							<label className="label">
								<span className="label-text">CQ Level (0-30)</span>
							</label>
							<input
								type="number"
								name="cqLevel"
								value={settings.cqLevel}
								onChange={handleChange}
								min="0"
								max="30"
								className="input input-bordered"
							/>
						</div>
					)}

					{/* Encoder */}
					<div className="form-control">
						<label className="label">
							<span className="label-text">Video Encoder</span>
						</label>
						<select
							name="encoder"
							value={settings.encoder}
							onChange={handleChange}
							className="select select-bordered"
						>
							<option value="gpu">GPU</option>
							<option value="cpu">CPU</option>
						</select>
					</div>

					{/* Codec */}
					<div className="form-control">
						<label className="label">
							<span className="label-text">Codec</span>
						</label>
						<select
							name="codec"
							value={settings.codec}
							onChange={handleChange}
							className="select select-bordered"
						>
							<option value="h264">H.264</option>
							<option value="h265">H.265</option>
						</select>
					</div>
				</div>
			</div>

			{/* Storage Settings */}
			<div className="p-4 bg-base-300 rounded-lg shadow-md">
				<h2 className="text-xl font-semibold mb-4">Storage Settings</h2>
				<div className="grid grid-cols-2 gap-4">
					{/* Recording Path */}
					<div className="form-control">
						<label className="label">
							<span className="label-text">Recording Path</span>
						</label>
						<div className="flex space-x-2">
							<input
								type="text"
								name="contentFolder"
								value={settings.contentFolder}
								onChange={handleChange}
								placeholder="Enter or select folder path"
								className="input input-bordered flex-1"
							/>
							<button onClick={handleBrowseClick} className="btn btn-neutral font-semibold">
								Browse
							</button>
						</div>
					</div>

					{/* Storage Limit */}
					<div className="form-control">
						<label className="label">
							<span className="label-text">Storage Limit (GB)</span>
						</label>
						<input
							type="number"
							name="storageLimit"
							value={localStorageLimit}
							onChange={(e) => setLocalStorageLimit(Number(e.target.value))}
							onBlur={() => updateSettings({storageLimit: localStorageLimit})}
							placeholder="Set maximum storage in GB"
							min="1"
							className="input input-bordered"
						/>
					</div>
				</div>
			</div>

			{/* Input/Output Devices */}
			<div className="p-4 bg-base-300 rounded-lg shadow-md">
				<h2 className="text-xl font-semibold mb-4">Input/Output Devices</h2>
				<div className="grid grid-cols-2 gap-4">
					{/* Input Device */}
					<div className="form-control">
						<label className="label">
							<span className="label-text">Input Device</span>
						</label>
						<div className="relative">
							<select
								name="inputDevice"
								value={settings.inputDevice}
								onChange={handleChange}
								className="select select-bordered w-full"
							>
								<option value={''}>
									Select Input Device
								</option>
								{/* If the selected device is not available, show it with a warning */}
								{!isDeviceAvailable(settings.inputDevice, settings.state.inputDevices) && settings.inputDevice && (
									<option value={settings.inputDevice}>
										⚠️ &lrm;
										{inputDeviceName}
									</option>
								)}
								{/* List available input devices */}
								{settings.state.inputDevices.map((device) => (
									<option key={device.id} value={device.id}>
										{device.name}
									</option>
								))}
							</select>
						</div>
					</div>

					{/* Output Device */}
					<div className="form-control">
						<label className="label">
							<span className="label-text">Output Device</span>
						</label>
						<div className="relative">
							<select
								name="outputDevice"
								value={settings.outputDevice}
								onChange={handleChange}
								className="select select-bordered w-full"
							>
								<option value={''}>
									Select Output Device
								</option>
								{/* If the selected device is not available, show it with a warning */}
								{!isDeviceAvailable(settings.outputDevice, settings.state.outputDevices) && settings.outputDevice && (
									<option value={settings.outputDevice}>
										⚠️ &lrm;
										{outputDeviceName}
									</option>
								)}
								{/* List available output devices */}
								{settings.state.outputDevices.map((device) => (
									<option key={device.id} value={device.id}>
										{device.name}
									</option>
								))}
							</select>
						</div>
					</div>
				</div>
			</div>

			{/* Advanced Settings */}
			<div className="p-4 bg-base-300 rounded-lg shadow-md">
				<h2 className="text-xl font-semibold mb-4">Advanced Settings</h2>
				<div className="bg-base-100 p-4 rounded-lg">
					<div className="flex items-center justify-between">
						<div className="flex items-center gap-2">
							<span className="font-medium">Enable Display Recording</span>
							<div className="badge badge-warning badge-sm">Alpha</div>
						</div>
						<input
							type="checkbox"
							name="enableDisplayRecording"
							checked={settings.enableDisplayRecording}
							onChange={(e) => updateSettings({enableDisplayRecording: e.target.checked})}
							className="toggle toggle-warning"
						/>
					</div>

					<div className="mt-3 bg-amber-900 bg-opacity-30 border border-amber-500 rounded px-3 py-2 text-amber-400 text-sm flex items-center">
						<MdWarning className="h-5 w-5 mr-2 flex-shrink-0" />
						<span>
							This feature enables recording of games that do not support game hook.
							<strong className="text-amber-300"> WARNING: This WILL cause lag</strong> during gameplay as it uses display capture instead of game capture.
							For more details, see <a href="https://github.com/Segergren/Segra/issues/1" target="_blank" rel="noopener noreferrer" className="text-amber-300 hover:text-amber-200 underline">GitHub Issue #1</a>.
						</span>
					</div>
				</div>
			</div>

			{/* Version */}
			<div className="text-center mt-4 text-sm text-gray-500">
				<div className="flex flex-col items-center gap-2">
					<button
						onClick={() => openReleaseNotesModal(null)}
						className="btn btn-sm btn-ghost text-gray-400 hover:text-gray-300"
					>
						View Release Notes
					</button>
					<div>Segra {__APP_VERSION__}</div>
				</div>
			</div>
		</div>
	);
}
