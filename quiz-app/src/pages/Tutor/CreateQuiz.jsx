const CreateQuiz = ({ isDark }) => {
  return (
    <div className="p-8">
      <h1 className={`text-4xl font-bold ${isDark ? 'text-white' : 'text-gray-900'}`}>
        Create New Quiz
      </h1>
    </div>
  );
};

export default CreateQuiz;
