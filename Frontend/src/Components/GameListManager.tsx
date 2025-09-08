import React, { useState, useEffect } from 'react';
import { Game } from '../Models/types';
import { useSettings } from '../Context/SettingsContext';
import { sendMessageToBackend } from '../Utils/MessageUtils';
import { isSelectedGameExecutableMessage } from '../Models/WebSocketMessages';
import { MdClose, MdAdd } from 'react-icons/md';

interface GameListManagerProps {
  listType: 'whitelist' | 'blacklist';
}

export const GameListManager: React.FC<GameListManagerProps> = ({ listType }) => {
  const settings = useSettings();

  const [newGameName, setNewGameName] = useState('');
  const [newGamePath, setNewGamePath] = useState('');
  const [isAdding, setIsAdding] = useState(false);
  const [isSelectingFile, setIsSelectingFile] = useState(false);

  const gameList = listType === 'whitelist' ? settings.whitelist : settings.blacklist;
  const listTitle = listType === 'whitelist' ? 'Allow List' : 'Block List';
  const listDescription =
    listType === 'whitelist'
      ? 'If Segra doesn\'t auto-detect a game, add its executable here to force detection.'
      : 'Add game executables here to prevent Segra from recording or detecting them.';
  const emptyListLabel = listType === 'whitelist' ? 'allow list' : 'block list';

  useEffect(() => {
    const handleWebSocketMessage = (event: CustomEvent<any>) => {
      const message = event.detail;

      if (isSelectingFile && isSelectedGameExecutableMessage(message)) {
        const selectedGame = message.content as Game;
        setNewGameName(selectedGame.name);
        setNewGamePath(selectedGame.path);
        setIsSelectingFile(false);
        setIsAdding(true); // Show the form after file is selected
      }
    };

    window.addEventListener('websocket-message', handleWebSocketMessage as EventListener);

    return () => {
      window.removeEventListener('websocket-message', handleWebSocketMessage as EventListener);
    };
  }, [isSelectingFile]);

  const handleSelectExecutable = () => {
    setIsSelectingFile(true);
    sendMessageToBackend('SelectGameExecutable');
  };

  const handleAddGame = () => {
    if (!newGameName.trim() || !newGamePath.trim()) return;

    const newGame: Game = {
      name: newGameName.trim(),
      path: newGamePath.trim()
    };

    // Send to backend
    sendMessageToBackend(
      listType === 'whitelist' ? 'AddToWhitelist' : 'AddToBlacklist',
      { game: newGame }
    );

    // Reset form
    setNewGameName('');
    setNewGamePath('');
    setIsAdding(false);
  };

  const handleRemoveGame = (game: Game) => {
    sendMessageToBackend(
      listType === 'whitelist' ? 'RemoveFromWhitelist' : 'RemoveFromBlacklist',
      { game }
    );
  };

  return (
    <div>
      <div className="mb-3">
        <h2 className="text-lg font-semibold">{listTitle}</h2>
        <p className="text-xs opacity-70 mt-1">{listDescription}</p>
      </div>

      <div className="mb-4">
        <div className="overflow-x-auto rounded-lg border border-base-400">
          <table className="table w-full">
            <thead className="bg-base-200">
              <tr>
                <th className="font-bold text-base-content w-1/3">Game</th>
                <th className="font-bold text-base-content w-1/2">Executable Path</th>
                <th className="font-bold text-base-content w-1/6">Action</th>
              </tr>
            </thead>
            <tbody>
              {gameList.length === 0 ? (
                <tr>
                  <td colSpan={3} className="text-center text-gray-500 py-4 border-b border-base-200">
                    No games in {emptyListLabel}
                  </td>
                </tr>
              ) : (
                gameList.map((game, index) => (
                  <tr key={index} className="border-b border-base-200">
                    <td className="font-medium">{game.name}</td>
                    <td className="text-xs text-gray-400 truncate max-w-xs">{game.path}</td>
                    <td>
                      <button
                        className="btn btn-sm btn-secondary border-base-400 hover:border-base-400"
                        onClick={() => handleRemoveGame(game)}
                      >
                        Remove
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      {!isAdding && (
        <div className="flex justify-start mt-4">
          <button
            className="btn btn-sm btn-secondary border-base-400 hover:border-base-400"
            onClick={handleSelectExecutable}
          >
            <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor" className="w-5 h-5 mr-1">
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
            </svg>
            Add Game
          </button>
        </div>
      )}

      {isAdding && (
        <div className="bg-base-200 rounded-lg p-4 mt-4 border border-base-400">
          <h3 className="text-md font-semibold mb-3">Add Game</h3>
          <div className="grid grid-cols-1 gap-3">
            <div>
              <label className="label pb-1">
                <span className="label-text text-base-content">Game Name</span>
              </label>
              <input
                type="text"
                className="input input-bordered w-full bg-base-200"
                value={newGameName}
                onChange={(e) => setNewGameName(e.target.value)}
                placeholder="Enter game name"
              />
            </div>
            <div>
              <label className="label pb-1">
                <span className="label-text text-base-content">Game Executable</span>
              </label>
              <div className="join w-full">
                <input
                  type="text"
                  className="input input-bordered bg-base-200 w-full join-item"
                  value={newGamePath}
                  onChange={(e) => setNewGamePath(e.target.value)}
                  placeholder="e.g. C:\\Program Files\\Game\\Game.exe"
                />
                <button
                  className="btn btn-secondary join-item border-custom border-opacity-75 hover:border-custom"
                  onClick={handleSelectExecutable}
                  disabled={isSelectingFile}
                >
                  {isSelectingFile ? (
                    <span className="loading loading-spinner loading-xs"></span>
                  ) : (
                    'Browse EXE'
                  )}
                </button>
              </div>
            </div>
          </div>
          <div className="flex justify-start gap-2 mt-4">
            <button
              className="btn btn-sm btn-primary flex items-center gap-1 text-base-300"
              onClick={handleAddGame}
              disabled={!newGameName.trim() || !newGamePath.trim()}
            >
              <MdAdd size={20} />
              Add
            </button>
            <button
              className="btn btn-sm btn-ghost flex items-center gap-1"
              onClick={() => {
                setIsAdding(false);
                setIsSelectingFile(false);
              }}
            >
              <MdClose size={20} />
              Cancel
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

export default GameListManager;
