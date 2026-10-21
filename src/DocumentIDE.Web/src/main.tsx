import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { installRestartFlushBridge } from './features/markdown/markdownRestartFlush'

// The desktop host calls this before the auto-updater stops the process.
installRestartFlushBridge()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
