import { createContext, ReactNode, useEffect, useState } from 'react';
import { api, getApiError } from '../services/Api';
type Signup = { name: string; organization: string; email: string; password: string };
type AuthContextType = {
    user: string | null; role: string; token: string | null; authenticated: boolean;
    error: string | null; setError: (error: string | null) => void;
    activeButton: boolean; setActiveButton: (active: boolean) => void;
    signIn: (email: string, password: string) => Promise<void>;
    signUp: (data: Signup) => Promise<void>; signOut: () => void;
};
export const AuthContext = createContext<AuthContextType>(null!);
export function AuthProvider({ children }: { children: ReactNode }) {
    const [user, setUser] = useState<string | null>(() => sessionStorage.getItem('neotasks:user'));
    const [token, setToken] = useState<string | null>(() => sessionStorage.getItem('neotasks:token'));
    const [role, setRole] = useState(() => sessionStorage.getItem('neotasks:role') || 'Member');
    const [error, setError] = useState<string | null>(null);
    const [activeButton, setActiveButton] = useState(true);
    function signOut() {
        void api.post("/logout").catch(()=>{});
        ['user', 'token', 'role'].forEach(key => sessionStorage.removeItem('neotasks:' + key));
        setUser(null); setToken(null); setRole('Member'); setError(null); setActiveButton(true);
    }
    useEffect(() => {
        const expired = () => { signOut(); setError('Sua sessão expirou. Entre novamente.'); };
        window.addEventListener('neotasks:session-expired', expired);
        return () => window.removeEventListener('neotasks:session-expired', expired);
    }, []);
    async function authenticate(path: string, data: object) {
        setActiveButton(false); setError(null);
        try {
            const { data: session } = await api.post(path, data);
            sessionStorage.setItem('neotasks:token', session.token);
            sessionStorage.setItem('neotasks:user', session.username);
            sessionStorage.setItem('neotasks:role', session.role);
            setUser(session.username); setToken(session.token); setRole(session.role);
        } catch (error) { setError(getApiError(error)); }
        finally { setActiveButton(true); }
    }
    return <AuthContext.Provider value={{ user, role, token, authenticated: !!user && !!token, error, setError, activeButton, setActiveButton,
        signIn: (email, password) => authenticate('/login', { email, password }),
        signUp: data => authenticate('/register', data), signOut }}>{children}</AuthContext.Provider>;
}
