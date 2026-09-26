import { useMemo, useState } from 'react';
import { Bookmark, BookmarkType } from '../Models/types';

/** Which bookmark types the timeline shows, and the bookmarks left after filtering. */
export function useBookmarkFilter(bookmarks: Bookmark[]) {
  const [selectedBookmarkTypes, setSelectedBookmarkTypes] = useState<Set<BookmarkType>>(
    new Set(Object.values(BookmarkType)),
  );

  const availableBookmarkTypes = useMemo(() => {
    const order = [
      BookmarkType.Kill,
      BookmarkType.Goal,
      BookmarkType.Assist,
      BookmarkType.Death,
      BookmarkType.Manual,
    ];
    return order.filter((type) => bookmarks.some((b) => b.type === type));
  }, [bookmarks]);

  const filteredBookmarks = useMemo(() => {
    return bookmarks.filter((bookmark) => selectedBookmarkTypes.has(bookmark.type));
  }, [bookmarks, selectedBookmarkTypes]);

  const toggleBookmarkType = (type: BookmarkType) => {
    setSelectedBookmarkTypes((prev) => {
      const newSet = new Set(prev);
      if (newSet.has(type)) {
        newSet.delete(type);
      } else {
        newSet.add(type);
      }
      return newSet;
    });
  };

  return { selectedBookmarkTypes, availableBookmarkTypes, filteredBookmarks, toggleBookmarkType };
}
