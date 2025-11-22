const TutorQuizzes = ({ isDark }) => {
  return (
    <div className="p-8">
      <h1 className={`text-4xl font-bold ${isDark ? 'text-white' : 'text-gray-900'}`}>
        Manage Quizzes
      </h1>
    </div>
  );
};

export default TutorQuizzes;
