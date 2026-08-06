import React from 'react';
import { GameSetting } from '../Models/types';
import { useSettings } from '../Context/SettingsContext';
import { sendMessageToBackend } from '../Utils/MessageUtils';
import Button from './Button';

interface GameListManagerProps {
  listType: 'whitelist' | 'blacklist';
}

/**
 * Legacy allow/block list view. Game detection UI now uses the unified `games` list
 * (GameDetectionSection); this component remains for any residual callers.
 */
export const GameListManager: React.FC<GameListManagerProps> = ({ listType }) => {
  const settings = useSettings();

  const gameList: GameSetting[] = (settings.games ?? []).filter((game) =>
    listType === 'whitelist' ? game.record : !game.record,
  );
  const listTitle = listType === 'whitelist' ? 'Allow List' : 'Block List';
  const listDescription =
    listType === 'whitelist'
      ? 'Games in your allow list are forced to be detected and recorded.'
      : 'Games in your block list are prevented from being recorded.';
  const emptyListLabel = listType === 'whitelist' ? 'allow list' : 'block list';

  const handleRemoveGame = (game: GameSetting) => {
    sendMessageToBackend('UpdateGames', {
      games: (settings.games ?? []).filter(
        (g) => !(g.name === game.name && JSON.stringify(g.paths) === JSON.stringify(game.paths)),
      ),
    });
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
                <th className="font-bold text-base-content w-1/6"></th>
              </tr>
            </thead>
            <tbody>
              {gameList.length === 0 ? (
                <tr>
                  <td
                    colSpan={3}
                    className="text-center text-gray-500 py-4 border-b border-base-200"
                  >
                    No games in {emptyListLabel}
                  </td>
                </tr>
              ) : (
                gameList.map((game, index) => (
                  <tr key={index} className="border-b border-base-200">
                    <td className="font-medium">{game.name}</td>
                    <td className="text-xs text-gray-400 max-w-xs">
                      {game.paths && game.paths.length > 1 ? (
                        <div>
                          <div className="font-semibold">{game.paths.length} executables</div>
                          {game.paths.map((path, idx) => (
                            <div key={idx} className="truncate">
                              • {path}
                            </div>
                          ))}
                        </div>
                      ) : (
                        <div className="truncate">{game.paths?.[0] || ''}</div>
                      )}
                    </td>
                    <td>
                      <Button variant="primary" size="sm" onClick={() => handleRemoveGame(game)}>
                        Remove
                      </Button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
};

export default GameListManager;
