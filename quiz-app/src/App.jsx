import { useState, useEffect } from 'react';
import { BrowserRouter as Router, Routes, Route, Navigate } from 'react-router-dom';
import Sidebar from './components/Sidebar';

// Student Pages
import StudentDashboard from './pages/Student/StudentDashboard';
import StudentQuizzes from './pages/Student/StudentQuizzes';
import TakeQuiz from './pages/Student/TakeQuiz';
import StudentAttempts from './pages/Student/StudentAttempts';

// Tutor Pages
import TutorDashboard from './pages/Tutor/TutorDashboard';
import TutorQuizzes from './pages/Tutor/TutorQuizzes';
import CreateQuiz from './pages/Tutor/CreateQuiz';
import ManageQuestions from './pages/Tutor/ManageQuestions';
import GradeAttempts from './pages/Tutor/GradeAttempts';

// Content Creator Pages
import CreatorDashboard from './pages/ContentCreator/CreatorDashboard';
import CreatorQuizzes from './pages/ContentCreator/CreatorQuizzes';
import CreatorCreateQuiz from './pages/ContentCreator/CreatorCreateQuiz';

// Landing Page
import RoleSelector from './pages/RoleSelector';

import './index.css';

function App() {
  const [isDark, setIsDark] = useState(() => {
    const saved = localStorage.getItem('darkMode');
    return saved ? JSON.parse(saved) : false;
  });

  useEffect(() => {
    localStorage.setItem('darkMode', JSON.stringify(isDark));
    if (isDark) {
      document.documentElement.classList.add('dark');
    } else {
      document.documentElement.classList.remove('dark');
    }
  }, [isDark]);

  const toggleTheme = () => setIsDark(!isDark);

  return (
    <Router>
      <Routes>
        {/* Landing Page - Role Selection */}
        <Route path="/" element={<RoleSelector isDark={isDark} toggleTheme={toggleTheme} />} />

        {/* STUDENT ROUTES */}
        <Route path="/student/*" element={
          <div className={`flex h-screen ${isDark ? 'bg-gray-950' : 'bg-gray-50'}`}>
            <Sidebar isDark={isDark} toggleTheme={toggleTheme} role="student" />
            <main className="flex-1 overflow-auto">
              <Routes>
                <Route path="/" element={<Navigate to="/student/dashboard" replace />} />
                <Route path="/dashboard" element={<StudentDashboard isDark={isDark} />} />
                <Route path="/quizzes" element={<StudentQuizzes isDark={isDark} />} />
                <Route path="/quiz/:quizId" element={<TakeQuiz isDark={isDark} />} />
                <Route path="/attempts" element={<StudentAttempts isDark={isDark} />} />
              </Routes>
            </main>
          </div>
        } />

        {/* TUTOR ROUTES */}
        <Route path="/tutor/*" element={
          <div className={`flex h-screen ${isDark ? 'bg-gray-950' : 'bg-gray-50'}`}>
            <Sidebar isDark={isDark} toggleTheme={toggleTheme} role="tutor" />
            <main className="flex-1 overflow-auto">
              <Routes>
                <Route path="/" element={<Navigate to="/tutor/dashboard" replace />} />
                <Route path="/dashboard" element={<TutorDashboard isDark={isDark} />} />
                <Route path="/quizzes" element={<TutorQuizzes isDark={isDark} />} />
                <Route path="/quiz/create" element={<CreateQuiz isDark={isDark} />} />
                <Route path="/quiz/:quizId/questions" element={<ManageQuestions isDark={isDark} />} />
                <Route path="/grading" element={<GradeAttempts isDark={isDark} />} />
              </Routes>
            </main>
          </div>
        } />

        {/* CONTENT CREATOR ROUTES */}
        <Route path="/creator/*" element={
          <div className={`flex h-screen ${isDark ? 'bg-gray-950' : 'bg-gray-50'}`}>
            <Sidebar isDark={isDark} toggleTheme={toggleTheme} role="content_creator" />
            <main className="flex-1 overflow-auto">
              <Routes>
                <Route path="/" element={<Navigate to="/creator/dashboard" replace />} />
                <Route path="/dashboard" element={<CreatorDashboard isDark={isDark} />} />
                <Route path="/quizzes" element={<CreatorQuizzes isDark={isDark} />} />
                <Route path="/quiz/create" element={<CreatorCreateQuiz isDark={isDark} />} />
              </Routes>
            </main>
          </div>
        } />

        {/* Fallback */}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </Router>
  );
}

export default App;
