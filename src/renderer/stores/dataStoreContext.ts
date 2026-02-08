import { createContext, createElement, useCallback, useContext, useMemo, useState } from 'react';

type DataStoreState = Record<string, unknown>;

type DataStoreContextValue = {
  data: DataStoreState;
  setData: (next: DataStoreState) => void;
  updateData: (path: string, value: unknown) => void;
};

const DataStoreContext = createContext<DataStoreContextValue | null>(null);

export function DataStoreProvider({
  children,
  initialData = {},
}: {
  children: React.ReactNode;
  initialData?: DataStoreState;
}) {
  const [data, setData] = useState<DataStoreState>(initialData);

  const updateData = useCallback((path: string, value: unknown) => {
    if (!path) return;
    const segments = path.split('.');
    setData((prev) => {
      const next: DataStoreState = { ...prev };
      let cursor: Record<string, unknown> = next;
      for (let i = 0; i < segments.length - 1; i += 1) {
        const key = segments[i];
        const existing = cursor[key];
        if (existing && typeof existing === 'object' && !Array.isArray(existing)) {
          cursor = existing as Record<string, unknown>;
        } else {
          const child: Record<string, unknown> = {};
          cursor[key] = child;
          cursor = child;
        }
      }
      cursor[segments[segments.length - 1]] = value;
      return next;
    });
  }, []);

  const value = useMemo<DataStoreContextValue>(() => ({ data, setData, updateData }), [data, updateData]);

  return createElement(DataStoreContext.Provider, { value }, children);
}

export function useDataStore() {
  const ctx = useContext(DataStoreContext);
  if (!ctx) {
    throw new Error('useDataStore must be used within DataStoreProvider');
  }
  return ctx;
}

export function useDataValue(path?: string) {
  const { data } = useDataStore();

  if (!path) return undefined;
  const segments = path.split('.');
  let current: unknown = data;
  for (const segment of segments) {
    if (current && typeof current === 'object' && !Array.isArray(current)) {
      current = (current as Record<string, unknown>)[segment];
    } else {
      return undefined;
    }
  }
  return current;
}
