import path from 'node:path';

export default {
  'Frontend/**/*.{ts,tsx,js,jsx,css,md,json,html}': (files) => {
    const quoted = files.map((f) => `"${f}"`).join(' ');
    return [
      `Frontend/node_modules/.bin/prettier --write ${quoted}`,
      `Frontend/node_modules/.bin/eslint --config Frontend/eslint.config.js --fix ${quoted}`,
    ];
  },
  '**/*.cs': (files) => {
    const quoted = files.map((f) => `"${path.relative(process.cwd(), f)}"`).join(' ');
    // Format only the staged files so another agent's uncommitted .cs work is left untouched
    return [`dotnet format Segra.sln --include ${quoted}`];
  },
};
