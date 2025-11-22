import axios from 'axios';

// Configure the base URL for your Azure Functions API
const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:7071/api';

const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
    'Cache-Control': 'no-cache, no-store, must-revalidate',
    'Pragma': 'no-cache',
    'Expires': '0',
  },
});

// Add request interceptor for adding API key if needed
apiClient.interceptors.request.use(
  (config) => {
    // If you have an API key for write operations, add it here
    const apiKey = localStorage.getItem('apiKey');
    if (apiKey && ['post', 'put', 'delete'].includes(config.method?.toLowerCase())) {
      config.headers['X-API-Key'] = apiKey;
    }
    return config;
  },
  (error) => {
    return Promise.reject(error);
  }
);

// ==================== QUIZ API ====================
export const quizApi = {
  // Get all quizzes with optional filters
  getQuizzes: async (params = {}) => {
    try {
      const response = await apiClient.get('/quizzes', { params });
      return response.data;
    } catch (error) {
      console.error('Error fetching quizzes:', error);
      throw error;
    }
  },

  // Get a single quiz by ID
  getQuizById: async (quizId) => {
    try {
      const response = await apiClient.get(`/quizzes/${quizId}`);
      return response.data;
    } catch (error) {
      console.error('Error fetching quiz:', error);
      throw error;
    }
  },

  // Get questions for a specific quiz
  getQuizQuestions: async (quizId) => {
    try {
      const response = await apiClient.get(`/quizzes/${quizId}/questions`);
      return response.data;
    } catch (error) {
      console.error('Error fetching quiz questions:', error);
      throw error;
    }
  },

  // Create a new quiz
  createQuiz: async (quizData) => {
    try {
      const response = await apiClient.post('/quizzes', quizData);
      return response.data;
    } catch (error) {
      console.error('Error creating quiz:', error);
      throw error;
    }
  },

  // Update a quiz
  updateQuiz: async (quizId, quizData) => {
    try {
      const response = await apiClient.put(`/quizzes/${quizId}`, quizData);
      return response.data;
    } catch (error) {
      console.error('Error updating quiz:', error);
      throw error;
    }
  },

  // Delete a quiz
  deleteQuiz: async (quizId) => {
    try {
      const response = await apiClient.delete(`/quizzes/${quizId}`);
      return response.data;
    } catch (error) {
      console.error('Error deleting quiz:', error);
      throw error;
    }
  },
};

// ==================== QUESTION API ====================
export const questionApi = {
  // Get all questions
  getQuestions: async (params = {}) => {
    try {
      const response = await apiClient.get('/questions', { params });
      return response.data;
    } catch (error) {
      console.error('Error fetching questions:', error);
      throw error;
    }
  },

  // Get a single question by ID
  getQuestionById: async (questionId) => {
    try {
      const response = await apiClient.get(`/questions/${questionId}`);
      return response.data;
    } catch (error) {
      console.error('Error fetching question:', error);
      throw error;
    }
  },

  // Create a new question
  createQuestion: async (questionData) => {
    try {
      const response = await apiClient.post('/questions', questionData);
      return response.data;
    } catch (error) {
      console.error('Error creating question:', error);
      throw error;
    }
  },

  // Delete a question
  deleteQuestion: async (questionId) => {
    try {
      const response = await apiClient.delete(`/questions/${questionId}`);
      return response.data;
    } catch (error) {
      console.error('Error deleting question:', error);
      throw error;
    }
  },
};

// ==================== ATTEMPT API ====================
export const attemptApi = {
  // Start a new quiz attempt
  startAttempt: async (quizId, userId, metadata = {}) => {
    try {
      const response = await apiClient.post('/attempts', {
        quizId,
        userId,
        metadata,
      });
      return response.data;
    } catch (error) {
      console.error('Error starting attempt:', error);
      throw error;
    }
  },

  // Get attempt by ID
  getAttemptById: async (attemptId) => {
    try {
      const response = await apiClient.get(`/attempts/${attemptId}`);
      return response.data;
    } catch (error) {
      console.error('Error fetching attempt:', error);
      throw error;
    }
  },

  // Get all attempts for a user
  getUserAttempts: async (userId) => {
    try {
      // Add cache-busting timestamp to force fresh data
      const response = await apiClient.get('/attempts', {
        params: { 
          userId,
          _t: Date.now() // Cache buster
        },
      });
      console.log('[API] getUserAttempts raw response:', response.data);
      console.log('[API] Attempt count:', response.data.attempts?.length);
      console.log('[API] Sample statuses:', response.data.attempts?.slice(0, 10).map(a => ({
        id: a.attemptId?.substring(0, 8),
        status: a.status,
        completed_at: a.completedAt
      })));
      return response.data;
    } catch (error) {
      console.error('Error fetching user attempts:', error);
      throw error;
    }
  },

  // Get responses for an attempt
  getAttemptResponses: async (attemptId) => {
    try {
      const response = await apiClient.get(`/attempts/${attemptId}/responses`);
      return response.data;
    } catch (error) {
      console.error('Error fetching attempt responses:', error);
      throw error;
    }
  },

  // Complete an attempt
  completeAttempt: async (attemptId) => {
    try {
      const response = await apiClient.post(`/attempts/${attemptId}/complete`);
      return response.data;
    } catch (error) {
      console.error('Error completing attempt:', error);
      throw error;
    }
  },
};

// ==================== RESPONSE API ====================
export const responseApi = {
  // Submit an answer
  submitAnswer: async (attemptId, questionId, answerPayload, pointsPossible) => {
    try {
      const response = await apiClient.post('/responses', {
        attemptId,
        questionId,
        answerPayload,
        pointsPossible,
      });
      return response.data;
    } catch (error) {
      console.error('Error submitting answer:', error);
      throw error;
    }
  },

  // Get response by ID
  getResponseById: async (responseId) => {
    try {
      const response = await apiClient.get(`/responses/${responseId}`);
      return response.data;
    } catch (error) {
      console.error('Error fetching response:', error);
      throw error;
    }
  },

  // Grade a response (for tutors)
  gradeResponse: async (responseId, pointsEarned, isCorrect, gradingDetails = {}) => {
    try {
      const response = await apiClient.post(`/responses/${responseId}/grade`, {
        pointsEarned,
        isCorrect,
        gradingDetails,
      });
      return response.data;
    } catch (error) {
      console.error('Error grading response:', error);
      throw error;
    }
  },
};

// ==================== HELPER FUNCTIONS ====================
export const helpers = {
  // Generate a simple user ID (for demo purposes)
  generateUserId: (role) => {
    const timestamp = Date.now();
    const random = Math.floor(Math.random() * 1000);
    return `${role}_${timestamp}_${random}`;
  },

  // Get or create user ID from localStorage
  getUserId: (role) => {
    const storageKey = `userId_${role}`;
    let userId = localStorage.getItem(storageKey);
    
    if (!userId) {
      userId = helpers.generateUserId(role);
      localStorage.setItem(storageKey, userId);
    }
    
    return userId;
  },

  // Calculate stats from attempts
  calculateAttemptStats: (attempts) => {
    if (!attempts || attempts.length === 0) {
      return {
        total: 0,
        completed: 0,
        inProgress: 0,
        averageScore: 0,
      };
    }

    const completed = attempts.filter(a => a.status === 'completed');
    const inProgress = attempts.filter(a => a.status === 'in_progress');
    
    const avgScore = completed.length > 0
      ? completed.reduce((sum, a) => sum + (a.scorePercentage || 0), 0) / completed.length
      : 0;

    return {
      total: attempts.length,
      completed: completed.length,
      inProgress: inProgress.length,
      averageScore: Math.round(avgScore),
    };
  },
};

export default apiClient;
