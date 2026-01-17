/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./src/renderer/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        // 新しいダークグレーベースのカラーテーマ（見やすさ重視）
        arsist: {
          bg: '#1e1e1e',           // VSCode風のダークグレー
          surface: '#252526',      // サーフェス
          border: '#3c3c3c',       // ボーダー
          hover: '#2d2d30',        // ホバー
          active: '#37373d',       // アクティブ
          primary: '#569cd6',      // プライマリ（落ち着いた青）
          accent: '#4ec9b0',       // アクセント（シアン/ミント）
          text: '#d4d4d4',         // メインテキスト
          muted: '#808080',        // サブテキスト
          success: '#4ec9b0',      // 成功
          warning: '#dcdcaa',      // 警告（黄色）
          error: '#f14c4c',        // エラー
        },
      },
      fontFamily: {
        sans: ['Inter', 'system-ui', 'sans-serif'],
        mono: ['JetBrains Mono', 'Consolas', 'monospace'],
      },
    },
  },
  plugins: [],
};
