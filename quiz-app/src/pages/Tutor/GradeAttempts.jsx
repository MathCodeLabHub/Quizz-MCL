const GradeAttempts = ({ isDark }) => {
  return (
    <div className="p-8">
      <h1 className={`text-4xl font-bold ${isDark ? 'text-white' : 'text-gray-900'}`}>
        Grade Student Submissions
      </h1>
    </div>
  );
};

export default GradeAttempts;
