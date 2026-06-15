import { useMemo } from 'react';
import { useSettings } from '../Context/SettingsContext';
import { Content, ContentType } from '../Models/types';
import type { SortOption } from '../Components/ContentFilters';

function getSectionId(contentType: ContentType): string {
  switch (contentType) {
    case 'Session':
      return 'sessions';
    case 'Buffer':
      return 'replayBuffer';
    case 'PendingEdit':
      return 'pendingEdit';
    case 'Clip':
      return 'clips';
    case 'Highlight':
      return 'highlights';
  }
}

function sortContentItems(items: Content[], sortOption: SortOption): Content[] {
  const sorted = [...items];
  sorted.sort((a, b) => {
    switch (sortOption) {
      case 'newest':
        return new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
      case 'oldest':
        return new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime();
      case 'size':
        return (b.fileSizeKb ?? 0) - (a.fileSizeKb ?? 0);
      case 'duration': {
        const toSecs = (dur: string) =>
          dur.split(':').reduce((acc, t) => 60 * acc + (parseInt(t, 10) || 0), 0);
        return toSecs(b.duration) - toSecs(a.duration);
      }
      case 'game': {
        const byGame = a.game.localeCompare(b.game);
        return byGame !== 0
          ? byGame
          : new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
      }
      default:
        return 0;
    }
  });
  return sorted;
}

export function useContentPlaylist(currentVideo: Content) {
  const { state } = useSettings();
  const sectionId = getSectionId(currentVideo.type);

  const playlist = useMemo(() => {
    const contentItems = state.content.filter((video) => video.type === currentVideo.type);

    let selectedGames: string[] = [];
    let sortOption: SortOption = 'newest';
    try {
      const savedFilters = localStorage.getItem(`${sectionId}-filters`);
      if (savedFilters) selectedGames = JSON.parse(savedFilters);
      const savedSort = localStorage.getItem(`${sectionId}-sort`);
      if (savedSort) sortOption = JSON.parse(savedSort);
    } catch {
      /* use defaults */
    }

    let filtered = [...contentItems];
    if (selectedGames.length > 0) {
      filtered = filtered.filter((item) => {
        if (selectedGames.includes('Imported') && item.isImported) return true;
        return selectedGames.filter((g) => g !== 'Imported').includes(item.game);
      });
    }

    return sortContentItems(filtered, sortOption);
  }, [state.content, currentVideo.type, sectionId]);

  const currentIndex = playlist.findIndex((v) => v.fileName === currentVideo.fileName);
  const prevVideo = currentIndex > 0 ? playlist[currentIndex - 1] : null;
  const nextVideo =
    currentIndex >= 0 && currentIndex < playlist.length - 1 ? playlist[currentIndex + 1] : null;

  return { playlist, currentIndex, prevVideo, nextVideo };
}
