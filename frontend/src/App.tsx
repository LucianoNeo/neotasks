import { BrowserRouter } from 'react-router-dom';
import { AuthProvider } from './contexts/AuthContext';
import { Provider } from './contexts/MyContext';

import Router from "./Router";

function App() {

  return (

    <AuthProvider>
      <Provider>
        <BrowserRouter>
          <Router />
        </BrowserRouter>
      </Provider>
    </AuthProvider>

  )
}

export default App
