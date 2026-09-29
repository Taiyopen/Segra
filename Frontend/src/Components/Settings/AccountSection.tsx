import React, { useEffect, useState } from 'react';
import { TriangleAlert, LogOut, Ellipsis, Mail, History } from 'lucide-react';
import { DiscordIcon } from '../icons/BrandIcons';
import discordLoading from '../../assets/discord-loading.webp';
import { useAuth } from '../../Hooks/useAuth';
import { useProfile } from '../../Hooks/useUserProfile';
import Button from '../Button';

type AuthTab = 'login' | 'register';

export default function AccountSection() {
  const {
    user,
    session,
    isAuthenticating,
    isWaitingForDiscord,
    authError,
    clearAuthError,
    login,
    register,
    loginWithDiscord,
    cancelDiscordLogin,
    signOut,
  } = useAuth();
  const { data: profile, error: profileError } = useProfile();
  const [error, setError] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [tab, setTab] = useState<AuthTab>('login');
  const [confirmEmailMessage, setConfirmEmailMessage] = useState('');
  const [lastUsedMethod, setLastUsedMethod] = useState<'discord' | 'email' | null>(null);

  useEffect(() => {
    const lastMethod = localStorage.getItem('lastLoginMethod') as 'discord' | 'email' | null;
    setLastUsedMethod(lastMethod);
  }, []);

  const handleDiscordLogin = () => {
    setError('');
    clearAuthError();
    localStorage.setItem('lastLoginMethod', 'discord');
    loginWithDiscord();
  };

  const handleEmailLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    clearAuthError();
    localStorage.setItem('lastLoginMethod', 'email');
    await login(email, password);
  };

  const handleRegister = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    clearAuthError();
    setConfirmEmailMessage('');

    if (password !== confirmPassword) {
      setError('兩次輸入的密碼不一樣');
      return;
    }

    if (password.length < 6) {
      setError('密碼至少要 6 個字元');
      return;
    }

    const result = await register(email, password);
    if (result?.confirmEmail) {
      setConfirmEmailMessage('請到信箱收信確認帳號，確認後再登入。');
    }
  };

  const handleLogout = async () => {
    signOut();
  };

  const displayError = error || authError;

  if (!session) {
    return (
      <div className="p-4 bg-base-300 rounded-lg shadow-md border border-custom space-y-4">
        {displayError && (
          <div className="alert alert-error" role="alert">
            <TriangleAlert className="w-5 h-5" />
            <span>{displayError}</span>
          </div>
        )}

        {confirmEmailMessage && (
          <div className="alert alert-success" role="alert">
            <span>{confirmEmailMessage}</span>
          </div>
        )}

        <div className="space-y-4">
          <div className="relative">
            {lastUsedMethod === 'discord' && (
              <div
                className={`absolute -top-3 right-2 bg-base-300 px-2 py-0.5 rounded-full border border-custom shadow-sm transition-all duration-300 ${isAuthenticating || isWaitingForDiscord ? 'opacity-0 scale-75' : 'opacity-100 scale-100'}`}
              >
                <div className="flex items-center gap-1 text-xs font-medium text-yellow-400">
                  <History className="w-3 h-3" />
                  上次使用
                </div>
              </div>
            )}
            <Button
              variant="primary"
              className="w-full gap-2 font-semibold text-white border-custom hover:border-custom"
              onClick={handleDiscordLogin}
              loading={isAuthenticating || isWaitingForDiscord}
              loadingIcon={
                isWaitingForDiscord ? (
                  // 30px, not 20px: the frame fits the full swirl, so the resting logo fills ~2/3.
                  <img
                    src={discordLoading}
                    alt=""
                    aria-hidden="true"
                    className="w-[30px] shrink-0"
                  />
                ) : undefined
              }
            >
              {!isWaitingForDiscord && <DiscordIcon className="w-5 h-5" />}
              {isWaitingForDiscord
                ? '等待瀏覽器完成登入…'
                : isAuthenticating
                  ? '連線中…'
                  : '用 Discord 登入'}
            </Button>
          </div>

          {isWaitingForDiscord && (
            <p className="-mt-2 text-center text-xs text-gray-400">
              請在瀏覽器裡完成 Discord 登入。{' '}
              <button
                type="button"
                className="underline underline-offset-2 hover:text-gray-200"
                onClick={cancelDiscordLogin}
              >
                取消
              </button>
            </p>
          )}

          <div className="divider">或使用電子郵件</div>

          {/* Tab toggle */}
          <div className="tabs tabs-boxed justify-center">
            <button
              className={`tab ${tab === 'login' ? 'tab-active' : ''}`}
              onClick={() => {
                setTab('login');
                setError('');
                setConfirmEmailMessage('');
              }}
            >
              登入
            </button>
            <button
              className={`tab ${tab === 'register' ? 'tab-active' : ''}`}
              onClick={() => {
                setTab('register');
                setError('');
                setConfirmEmailMessage('');
              }}
            >
              註冊
            </button>
          </div>

          {tab === 'login' ? (
            <form onSubmit={handleEmailLogin} className="space-y-4">
              <div className="form-control">
                <div className="mb-2">電子郵件</div>
                <input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className="input input-bordered bg-base-200 w-full"
                  disabled={isAuthenticating}
                  placeholder="example@example.com"
                  required
                />
              </div>

              <div className="form-control">
                <div className="mb-2">密碼</div>
                <input
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="input input-bordered bg-base-200 w-full"
                  disabled={isAuthenticating}
                  placeholder="********"
                  required
                />
              </div>

              <div className="relative">
                {lastUsedMethod === 'email' && (
                  <div
                    className={`absolute -top-3 right-2 bg-base-300 px-2 py-0.5 rounded-full border border-custom shadow-sm transition-all duration-300 ${isAuthenticating ? 'opacity-0 scale-75' : 'opacity-100 scale-100'}`}
                  >
                    <div className="flex items-center gap-1 text-xs font-medium text-yellow-400">
                      <History className="w-3 h-3" />
                      上次使用
                    </div>
                  </div>
                )}
                <Button
                  type="submit"
                  variant="primary"
                  className="w-full font-semibold text-white border-custom hover:border-custom"
                  loading={isAuthenticating}
                >
                  <Mail size={20} />
                  用電子郵件登入
                </Button>
              </div>
            </form>
          ) : (
            <form onSubmit={handleRegister} className="space-y-4">
              <div className="form-control">
                <div className="mb-2">電子郵件</div>
                <input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className="input input-bordered bg-base-200 w-full"
                  disabled={isAuthenticating}
                  placeholder="example@example.com"
                  required
                />
              </div>

              <div className="form-control">
                <div className="mb-2">密碼</div>
                <input
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="input input-bordered bg-base-200 w-full"
                  disabled={isAuthenticating}
                  placeholder="********"
                  required
                />
              </div>

              <div className="form-control">
                <div className="mb-2">確認密碼</div>
                <input
                  type="password"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  className="input input-bordered bg-base-200 w-full"
                  disabled={isAuthenticating}
                  placeholder="********"
                  required
                />
              </div>

              <Button
                type="submit"
                variant="primary"
                className="w-full font-semibold text-white border-custom hover:border-custom"
                loading={isAuthenticating}
              >
                <Mail size={20} />
                建立帳號
              </Button>
            </form>
          )}
        </div>
      </div>
    );
  }

  return (
    <div className="p-4 bg-base-300 rounded-lg shadow-md border border-custom">
      <div className="flex items-center justify-between flex-wrap gap-4">
        <div className="flex items-center gap-4 min-w-0">
          {/* Avatar Container */}
          <div className="relative w-16 h-16">
            <div className="w-full h-full rounded-full overflow-hidden bg-base-200 ring-2 ring-base-300">
              {profile?.avatar_url ? (
                <img
                  src={profile.avatar_url}
                  alt={`${profile.username} 的頭像`}
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
              {profile?.username && !profile.username.startsWith('user_') ? (
                profile.username
              ) : (
                <div className="skeleton h-[24px] w-24"></div>
              )}
            </h3>
            <p className="text-sm opacity-70 truncate">{user?.email || '已登入的使用者'}</p>
          </div>

          {/* More Options Dropdown */}
          <div className="dropdown">
            <label
              tabIndex={0}
              className="btn btn-ghost btn-sm btn-circle hover:bg-white/10 active:bg-white/10"
            >
              <Ellipsis size={24} />
            </label>
            <ul
              tabIndex={0}
              className="dropdown-content menu bg-base-300 border border-base-400 rounded-box z-999 w-52 p-2"
            >
              <li>
                <Button
                  variant="menuDanger"
                  onClick={() => {
                    (document.activeElement as HTMLElement).blur();
                    handleLogout();
                  }}
                >
                  <LogOut size={20} />
                  <span>登出</span>
                </Button>
              </li>
            </ul>
          </div>
        </div>
      </div>

      {/* Error State */}
      {profileError && (
        <div className="alert alert-error mt-3" role="alert" aria-live="assertive">
          <TriangleAlert className="w-5 h-5" />
          <div>
            <h3 className="font-bold">讀取個人資料失敗</h3>
            <div className="text-xs">{profileError.message || '發生未知錯誤'}</div>
          </div>
        </div>
      )}
    </div>
  );
}
